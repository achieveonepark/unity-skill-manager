using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Achieve.UnitySkillManager.Editor
{
    public sealed class SkillSynchronizer
    {
        private static readonly SkillTarget[] Targets = { SkillTarget.Codex, SkillTarget.Claude };
        public string ProjectRoot { get; }
        public SkillManagerSettings Settings { get; private set; }
        public string SettingsPath => SkillFiles.Under(ProjectRoot, "ProjectSettings/AchieveSkillManager.json");
        private string WorkRoot => SkillFiles.Under(ProjectRoot, "Temp/AchieveSkillManager");

        public SkillSynchronizer(string projectRoot)
        {
            ProjectRoot = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(Path.Combine(ProjectRoot, "Assets")) || !Directory.Exists(Path.Combine(ProjectRoot, "ProjectSettings")))
                throw new ArgumentException("Choose the Unity project root containing Assets and ProjectSettings.", nameof(projectRoot));
            Settings = File.Exists(SettingsPath) ? JsonUtility.FromJson<SkillManagerSettings>(File.ReadAllText(SettingsPath)) : new SkillManagerSettings();
            if (Settings == null || Settings.formatVersion != 1)
                throw new InvalidDataException("Unsupported Skill Manager settings format.");
            if (Settings.managed == null) Settings.managed = new List<ManagedSkill>();
            if (Settings.excludedSkills == null) Settings.excludedSkills = new List<string>();
            foreach (var item in Settings.managed)
                if (item == null || !SkillFiles.ValidName(item.skillName) || string.IsNullOrEmpty(item.packageName) ||
                    string.IsNullOrEmpty(item.fingerprint) || !Enum.IsDefined(typeof(SkillTarget), item.target))
                    throw new InvalidDataException("Invalid managed skill record. The settings file was not changed.");
            if (Settings.managed.GroupBy(item => new { item.skillName, item.target }).Any(group => group.Count() != 1))
                throw new InvalidDataException("Duplicate managed skill records. The settings file was not changed.");
        }

        public void SaveSettings() => SkillFiles.AtomicWrite(SettingsPath, JsonUtility.ToJson(Settings, true) + "\n");
        public bool IsIncluded(PackageSkill skill) => !Settings.excludedSkills.Contains(skill.Key);

        public void SetIncluded(PackageSkill skill, bool included)
        {
            Settings.excludedSkills.RemoveAll(key => key == skill.Key);
            if (!included) Settings.excludedSkills.Add(skill.Key);
            SaveSettings();
        }

        public string Destination(string skillName, SkillTarget target)
        {
            if (!SkillFiles.ValidName(skillName)) throw new ArgumentException("Invalid skill name.", nameof(skillName));
            if (!Enum.IsDefined(typeof(SkillTarget), target)) throw new ArgumentOutOfRangeException(nameof(target));
            return SkillFiles.Under(ProjectRoot, (target == SkillTarget.Codex ? ".agents/skills/" : ".claude/skills/") + skillName);
        }

        public SkillState GetState(PackageSkill skill, SkillTarget target)
        {
            if (!skill.IsValid) return SkillState.Invalid;
            var destination = Destination(skill.Name, target);
            if (File.Exists(destination)) return SkillState.Conflict;
            if (!Directory.Exists(destination)) return SkillState.Missing;
            var fingerprint = SkillFiles.Fingerprint(SkillFiles.ReadBundle(destination));
            var record = Settings.managed.Find(item => item.skillName == skill.Name && item.target == target);
            if (record == null) return fingerprint == skill.Fingerprint ? SkillState.UpdateAvailable : SkillState.Conflict;
            if (record.packageName != skill.Package.Name) return SkillState.Conflict;
            if (record.fingerprint != fingerprint) return SkillState.LocallyModified;
            return fingerprint == skill.Fingerprint && record.packageVersion == skill.Package.Version ? SkillState.Current : SkillState.UpdateAvailable;
        }

        public SkillSyncReport Synchronize(IReadOnlyList<PackageSkill> skills, bool removeStale = true)
        {
            var report = new SkillSyncReport();
            var duplicateNames = new HashSet<string>(skills.Where(s => s.IsValid).GroupBy(s => s.Name).Where(g => g.Count() > 1).Select(g => g.Key));
            foreach (var skill in skills.Where(IsIncluded))
            {
                if (!skill.IsValid || duplicateNames.Contains(skill.Name))
                {
                    report.Issues.Add(skill.Key + ": " + (skill.Error ?? "Multiple packages provide the same skill name."));
                    continue;
                }
                foreach (var target in Targets.Where(Settings.IsEnabled))
                {
                    try
                    {
                        var state = GetState(skill, target);
                        if (state == SkillState.Current) continue;
                        if (state == SkillState.Conflict || state == SkillState.LocallyModified)
                        {
                            report.Issues.Add(skill.Name + " (" + target + "): " + state + ". Existing files were preserved.");
                            continue;
                        }
                        Install(skill, target);
                        report.Updated++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                    { report.Issues.Add(skill.Name + " (" + target + "): " + ex.Message); }
                }
            }
            if (!removeStale) return report;
            // Invalid or colliding providers still count as present, so a scan error cannot delete their previous exports.
            var present = new HashSet<string>(skills.Where(IsIncluded).Select(s => s.Key));
            var scanFailed = new HashSet<string>(skills.Where(s => !s.IsValid).Select(s => s.Package.Name));
            foreach (var record in Settings.managed.ToArray())
            {
                if (Settings.IsEnabled(record.target) && (present.Contains(record.Key) || scanFailed.Contains(record.packageName))) continue;
                try { if (Remove(record, report)) report.Removed++; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { report.Issues.Add(record.skillName + " (" + record.target + "): " + ex.Message); }
            }
            return report;
        }

        public SkillSyncReport RemoveSkill(PackageSkill skill)
        {
            SetIncluded(skill, false);
            var report = new SkillSyncReport();
            foreach (var record in Settings.managed.Where(item => item.Key == skill.Key).ToArray())
            {
                try { if (Remove(record, report)) report.Removed++; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { report.Issues.Add(record.skillName + " (" + record.target + "): " + ex.Message); }
            }
            return report;
        }

        private void Install(PackageSkill skill, SkillTarget target)
        {
            var destination = Destination(skill.Name, target);
            var oldSettings = JsonUtility.ToJson(Settings);
            var stage = NewWorkFolder();
            var stagedFiles = SkillFiles.Under(stage, "new");
            var backup = SkillFiles.Under(stage, "previous");
            var installed = false;
            var backedUp = false;
            var canCleanUp = false;
            try
            {
                SkillFiles.WriteBundle(stagedFiles, skill.Files);
                var currentState = GetState(skill, target);
                if (currentState == SkillState.Conflict || currentState == SkillState.LocallyModified)
                    throw new IOException("The destination changed during synchronization. Existing files were preserved.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (Directory.Exists(destination)) { Directory.Move(destination, backup); backedUp = true; }
                Directory.Move(stagedFiles, destination);
                installed = true;
                Settings.managed.RemoveAll(item => item.skillName == skill.Name && item.target == target);
                Settings.managed.Add(new ManagedSkill
                {
                    packageName = skill.Package.Name, packageVersion = skill.Package.Version,
                    skillName = skill.Name, target = target, fingerprint = skill.Fingerprint
                });
                SaveSettings();
                canCleanUp = true;
            }
            catch
            {
                Settings = JsonUtility.FromJson<SkillManagerSettings>(oldSettings);
                if (installed) Directory.Move(destination, SkillFiles.Under(stage, "failed"));
                if (backedUp) Directory.Move(backup, destination);
                canCleanUp = true;
                throw;
            }
            finally { if (canCleanUp) DeleteWorkFolder(stage); }
        }

        private bool Remove(ManagedSkill record, SkillSyncReport report)
        {
            var destination = Destination(record.skillName, record.target);
            if (File.Exists(destination) || (Directory.Exists(destination) &&
                SkillFiles.Fingerprint(SkillFiles.ReadBundle(destination)) != record.fingerprint))
            {
                report.Issues.Add(record.skillName + " (" + record.target + "): LocallyModified. Existing files were preserved.");
                return false;
            }
            var oldSettings = JsonUtility.ToJson(Settings);
            var stage = NewWorkFolder();
            var backup = SkillFiles.Under(stage, "previous");
            var backedUp = false;
            var canCleanUp = false;
            try
            {
                if (Directory.Exists(destination)) { Directory.Move(destination, backup); backedUp = true; }
                Settings.managed.Remove(record);
                SaveSettings();
                canCleanUp = true;
            }
            catch
            {
                Settings = JsonUtility.FromJson<SkillManagerSettings>(oldSettings);
                if (backedUp) Directory.Move(backup, destination);
                canCleanUp = true;
                throw;
            }
            finally { if (canCleanUp) DeleteWorkFolder(stage); }
            return true;
        }

        private string NewWorkFolder()
        {
            var path = SkillFiles.Under(WorkRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private void DeleteWorkFolder(string path)
        {
            // Only manager-created transaction folders beneath this project's Temp can be removed recursively.
            var checkedPath = SkillFiles.Under(WorkRoot, Path.GetFileName(path));
            if (Path.GetFullPath(path) != checkedPath) throw new IOException("Unexpected transaction path.");
            if (!Directory.Exists(checkedPath)) return;
            try { Directory.Delete(checkedPath, true); }
            catch (IOException) { /* A locked temporary backup can be cleared with Unity's Temp folder. */ }
            catch (UnauthorizedAccessException) { /* Keep the backup if the operating system denies cleanup. */ }
        }
    }
}
