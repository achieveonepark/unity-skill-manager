using System;
using System.Collections.Generic;

namespace Achieve.UnitySkillManager.Editor
{
    public enum SkillTarget { Codex, Claude }
    public enum SkillState { Missing, Current, UpdateAvailable, LocallyModified, Conflict, Invalid }

    public sealed class SkillPackage
    {
        public string Name { get; }
        public string Version { get; }
        public string DisplayName { get; }
        public string ResolvedPath { get; }
        public SkillPackage(string name, string version, string resolvedPath, string displayName = null)
        {
            Name = name; Version = version; ResolvedPath = resolvedPath; DisplayName = displayName ?? name;
        }
    }

    public sealed class PackageSkill
    {
        public SkillPackage Package { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public string SourcePath { get; internal set; }
        public string Error { get; internal set; }
        public string Fingerprint { get; internal set; }
        // null: provided by Skills~ and exported to every enabled target. Otherwise provided by
        // <package>/.agents/skills (Codex) or <package>/.claude/skills (Claude) and exported only there.
        public SkillTarget? OnlyTarget { get; internal set; }
        public bool Applies(SkillTarget target) => OnlyTarget == null || OnlyTarget == target;
        public string Key => Package.Name + "/" + Name;
        public bool IsValid => string.IsNullOrEmpty(Error);
        internal SortedDictionary<string, byte[]> Files;
    }

    [Serializable]
    public sealed class ManagedSkill
    {
        public string packageName;
        public string packageVersion;
        public string skillName;
        public SkillTarget target;
        public string fingerprint;
        public string Key => packageName + "/" + skillName;
    }

    [Serializable]
    public sealed class SkillManagerSettings
    {
        public int formatVersion = 1;
        public bool autoSync = true;
        public bool codex = true;
        public bool claude = true;
        public List<string> excludedSkills = new List<string>();
        public List<ManagedSkill> managed = new List<ManagedSkill>();
        public bool IsEnabled(SkillTarget target) => target == SkillTarget.Codex ? codex : claude;
    }

    [Serializable]
    public sealed class SkillCacheEntry
    {
        public string packageName;
        public string packageVersion;
        public string skillName;
        public string target;
        public string path;
        public string fingerprint;
    }

    // Rebuildable record of what was exported, written to Library/ai_skill_list.json.
    [Serializable]
    public sealed class SkillCache
    {
        public int formatVersion = 1;
        public List<SkillCacheEntry> skills = new List<SkillCacheEntry>();
    }

    public sealed class SkillSyncReport
    {
        public int Updated { get; internal set; }
        public int Removed { get; internal set; }
        public readonly List<string> Issues = new List<string>();
        public bool Succeeded => Issues.Count == 0;
    }
}
