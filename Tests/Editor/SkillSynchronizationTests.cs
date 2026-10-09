using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Achieve.UnitySkillManager.Editor.Tests
{
    public sealed class SkillSynchronizationTests
    {
        private string root;
        private string testArea;
        private List<SkillPackage> packages;

        [SetUp]
        public void SetUp()
        {
            testArea = Path.Combine(Path.GetTempPath(), "AchieveSkillManagerTests");
            root = Path.Combine(testArea, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            packages = new List<SkillPackage>();
        }

        [TearDown]
        public void TearDown()
        {
            var checkedRoot = Path.GetFullPath(root);
            Assert.That(checkedRoot.StartsWith(Path.GetFullPath(testArea) + Path.DirectorySeparatorChar, StringComparison.Ordinal), Is.True);
            if (Directory.Exists(checkedRoot)) Directory.Delete(checkedRoot, true);
        }

        private string AddSkill(string packageName = "com.example.first", string skillName = "demo-skill", string header = null, string version = "1.0.0")
        {
            var packageRoot = Path.Combine(root, "Packages", packageName);
            packages.Add(new SkillPackage(packageName, version, packageRoot));
            var folder = Path.Combine(packageRoot, "Skills~", skillName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SKILL.md"), header ?? $"---\nname: {skillName}\ndescription: 게임 이벤트 연동 가이드\n---\n\n# Example\n", new UTF8Encoding(false));
            Directory.CreateDirectory(Path.Combine(folder, "references"));
            File.WriteAllText(Path.Combine(folder, "references", "guide.md"), "reference v1", new UTF8Encoding(false));
            return folder;
        }

        private List<PackageSkill> Scan() => SkillCatalog.Discover(packages);
        private string Export(string name, SkillTarget target) => new SkillSynchronizer(root).Destination(name, target);
        private string ReadExport(string name, SkillTarget target, string relative = "SKILL.md") => File.ReadAllText(Path.Combine(Export(name, target), relative));

        [TestCase("description: normal description")]
        [TestCase("description: 'quoted description'")]
        [TestCase("description: \"quoted description\"")]
        [TestCase("description: >-\n  folded\n  description")]
        [TestCase("description: |\n  literal\n  description")]
        public void FindsStandardAndMultilineFrontmatter(string description)
        {
            AddSkill(header: "---\nname: demo-skill\n" + description + "\n---\n# Body\n");
            var skill = Scan().Single();
            Assert.That(skill.IsValid, Is.True, skill.Error);
            Assert.That(skill.Description, Does.Contain("description"));
        }

        [TestCase("# no frontmatter")]
        [TestCase("---\nname: demo-skill\n---\n")]
        [TestCase("---\nname: ../../escape\ndescription: test\n---\n")]
        [TestCase("---\nname: different-name\ndescription: test\n---\n")]
        [TestCase("---\nname: demo-skill\ndescription: test\n")]
        public void InvalidMetadataIsReportedWithoutWritingFiles(string header)
        {
            AddSkill(header: header);
            var skill = Scan().Single();
            Assert.That(skill.IsValid, Is.False);
            var report = new SkillSynchronizer(root).Synchronize(Scan());
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(Directory.Exists(Path.Combine(root, ".agents")), Is.False);
        }

        [Test]
        public void ExportsCompleteBundlesToBothTargetsAndSurvivesReload()
        {
            var source = AddSkill();
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            var bytes = new byte[] { 0, 1, 127, 128, 255 };
            File.WriteAllBytes(Path.Combine(source, "assets", "sample.bin"), bytes);
            var sync = new SkillSynchronizer(root);
            Assert.That(sync.Synchronize(Scan()).Updated, Is.EqualTo(2));
            foreach (var target in new[] { SkillTarget.Codex, SkillTarget.Claude })
            {
                Assert.That(ReadExport("demo-skill", target), Is.EqualTo(File.ReadAllText(Path.Combine(source, "SKILL.md"))));
                Assert.That(ReadExport("demo-skill", target, "references/guide.md"), Is.EqualTo("reference v1"));
                Assert.That(File.ReadAllBytes(Path.Combine(Export("demo-skill", target), "assets/sample.bin")), Is.EqualTo(bytes));
            }
            var reloaded = new SkillSynchronizer(root);
            Assert.That(reloaded.Settings.managed, Has.Count.EqualTo(2));
            Assert.That(reloaded.Synchronize(Scan()).Updated, Is.Zero);
            Assert.That(reloaded.GetState(Scan()[0], SkillTarget.Codex), Is.EqualTo(SkillState.Current));
        }

        [Test]
        public void UpdatesBundleAndRemovesObsoleteFiles()
        {
            var source = AddSkill();
            new SkillSynchronizer(root).Synchronize(Scan());
            File.Delete(Path.Combine(source, "references/guide.md"));
            File.WriteAllText(Path.Combine(source, "references/new-guide.md"), "new reference");
            packages[0] = new SkillPackage(packages[0].Name, "2.0.0", packages[0].ResolvedPath);
            var sync = new SkillSynchronizer(root);
            Assert.That(sync.Synchronize(Scan()).Updated, Is.EqualTo(2));
            Assert.That(File.Exists(Path.Combine(Export("demo-skill", SkillTarget.Codex), "references/guide.md")), Is.False);
            Assert.That(ReadExport("demo-skill", SkillTarget.Claude, "references/new-guide.md"), Is.EqualTo("new reference"));
            Assert.That(sync.Settings.managed.All(item => item.packageVersion == "2.0.0"), Is.True);
        }

        [Test]
        public void PreservesEditedAndAdditionalUserFilesWhileUpdatingOtherTarget()
        {
            var source = AddSkill();
            new SkillSynchronizer(root).Synchronize(Scan());
            File.WriteAllText(Path.Combine(Export("demo-skill", SkillTarget.Codex), "notes.md"), "my notes");
            File.AppendAllText(Path.Combine(source, "SKILL.md"), "\npackage update");
            var sync = new SkillSynchronizer(root);
            Assert.That(sync.GetState(Scan()[0], SkillTarget.Codex), Is.EqualTo(SkillState.LocallyModified));
            var report = sync.Synchronize(Scan());
            Assert.That(report.Updated, Is.EqualTo(1));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(ReadExport("demo-skill", SkillTarget.Codex, "notes.md"), Is.EqualTo("my notes"));
            Assert.That(ReadExport("demo-skill", SkillTarget.Codex), Does.Not.Contain("package update"));
            Assert.That(ReadExport("demo-skill", SkillTarget.Claude), Does.Contain("package update"));
        }

        [Test]
        public void PreservesUnmanagedSkillWithSameName()
        {
            AddSkill();
            var destination = Export("demo-skill", SkillTarget.Codex);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "SKILL.md"), "user-owned skill");
            var report = new SkillSynchronizer(root).Synchronize(Scan());
            Assert.That(report.Updated, Is.EqualTo(1));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(ReadExport("demo-skill", SkillTarget.Codex), Is.EqualTo("user-owned skill"));
        }

        [Test]
        public void AdoptsIdenticalExistingBundle()
        {
            var source = AddSkill();
            var destination = Export("demo-skill", SkillTarget.Codex);
            Directory.CreateDirectory(Path.Combine(destination, "references"));
            File.Copy(Path.Combine(source, "SKILL.md"), Path.Combine(destination, "SKILL.md"));
            File.Copy(Path.Combine(source, "references/guide.md"), Path.Combine(destination, "references/guide.md"));
            var sync = new SkillSynchronizer(root);
            Assert.That(sync.Synchronize(Scan()).Succeeded, Is.True);
            Assert.That(sync.Settings.managed, Has.Count.EqualTo(2));
        }

        [Test]
        public void DuplicateProvidersDoNotOverwritePreviouslyRegisteredSkill()
        {
            AddSkill();
            new SkillSynchronizer(root).Synchronize(Scan());
            var original = ReadExport("demo-skill", SkillTarget.Codex);
            AddSkill("com.example.second");
            Assert.That(Scan().All(skill => !skill.IsValid), Is.True);
            var sync = new SkillSynchronizer(root);
            var report = sync.Synchronize(Scan());
            Assert.That(report.Updated, Is.Zero);
            Assert.That(report.Removed, Is.Zero);
            Assert.That(report.Issues, Has.Count.EqualTo(2));
            Assert.That(ReadExport("demo-skill", SkillTarget.Codex), Is.EqualTo(original));
        }

        [Test]
        public void RemovedPackageCleansOwnedSkillsButKeepsEditedFiles()
        {
            AddSkill();
            new SkillSynchronizer(root).Synchronize(Scan());
            File.AppendAllText(Path.Combine(Export("demo-skill", SkillTarget.Codex), "SKILL.md"), "\nuser edits");
            var sync = new SkillSynchronizer(root);
            var report = sync.Synchronize(new List<PackageSkill>());
            Assert.That(report.Removed, Is.EqualTo(1));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(Directory.Exists(Export("demo-skill", SkillTarget.Claude)), Is.False);
            Assert.That(ReadExport("demo-skill", SkillTarget.Codex), Does.Contain("user edits"));
            Assert.That(sync.Settings.managed, Has.Count.EqualTo(1));
        }

        [Test]
        public void ExplicitRemovalStaysExcludedAcrossSynchronization()
        {
            AddSkill();
            var sync = new SkillSynchronizer(root);
            var skills = Scan();
            sync.Synchronize(skills);
            Assert.That(sync.RemoveSkill(skills[0]).Removed, Is.EqualTo(2));
            Assert.That(new SkillSynchronizer(root).Synchronize(skills).Updated, Is.Zero);
            sync.SetIncluded(skills[0], true);
            Assert.That(sync.Synchronize(skills).Updated, Is.EqualTo(2));
        }

        [Test]
        public void TargetCanBeDisabledWithoutChangingOtherTarget()
        {
            AddSkill();
            var sync = new SkillSynchronizer(root);
            sync.Synchronize(Scan());
            sync.Settings.claude = false;
            sync.SaveSettings();
            Assert.That(sync.Synchronize(Scan()).Removed, Is.EqualTo(1));
            Assert.That(Directory.Exists(Export("demo-skill", SkillTarget.Codex)), Is.True);
            Assert.That(Directory.Exists(Export("demo-skill", SkillTarget.Claude)), Is.False);
            sync.Settings.claude = true;
            Assert.That(sync.Synchronize(Scan()).Updated, Is.EqualTo(1));
        }

        [Test]
        public void SelectedSynchronizationDoesNotPruneOtherSkills()
        {
            AddSkill(skillName: "first-skill");
            AddSkill("com.example.second", "second-skill");
            var sync = new SkillSynchronizer(root);
            var skills = Scan();
            sync.Synchronize(skills);
            var report = sync.Synchronize(new[] { skills[0] }, false);
            Assert.That(report.Removed, Is.Zero);
            Assert.That(Directory.Exists(Export("second-skill", SkillTarget.Claude)), Is.True);
        }

        [Test]
        public void FailedSettingsWriteRestoresBothPreviousBundles()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Tests Windows file-share locking semantics.");
            var source = AddSkill();
            var sync = new SkillSynchronizer(root);
            sync.Synchronize(Scan());
            var original = ReadExport("demo-skill", SkillTarget.Codex);
            File.AppendAllText(Path.Combine(source, "SKILL.md"), "\npackage update");
            using (var locked = File.Open(sync.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var report = sync.Synchronize(Scan());
                Assert.That(report.Updated, Is.Zero);
                Assert.That(report.Issues, Has.Count.EqualTo(2));
            }
            Assert.That(ReadExport("demo-skill", SkillTarget.Codex), Is.EqualTo(original));
            Assert.That(ReadExport("demo-skill", SkillTarget.Claude), Is.EqualTo(original));
            Assert.That(sync.Synchronize(Scan()).Updated, Is.EqualTo(2));
        }

        [Test]
        public void RejectsTraversalInStateFile()
        {
            var settings = new SkillManagerSettings();
            settings.managed.Add(new ManagedSkill { packageName = "com.example", skillName = "../../escape", fingerprint = "fake" });
            File.WriteAllText(Path.Combine(root, "ProjectSettings/AchieveSkillManager.json"), JsonUtility.ToJson(settings));
            Assert.Throws<InvalidDataException>(() => new SkillSynchronizer(root));
            Assert.That(Directory.Exists(Path.Combine(root, ".agents")), Is.False);
        }
    }
}
