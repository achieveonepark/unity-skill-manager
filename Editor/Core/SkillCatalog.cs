using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Achieve.UnitySkillManager.Editor
{
    public static class SkillCatalog
    {
        // Package-relative skill roots and the target each one is exported to (null = every enabled target).
        private static readonly (string Root, SkillTarget? Target)[] Roots =
        {
            ("Skills~", null),
            (".agents/skills", SkillTarget.Codex),
            (".claude/skills", SkillTarget.Claude),
        };

        public static List<PackageSkill> Discover(IEnumerable<SkillPackage> packages)
        {
            var result = new List<PackageSkill>();
            foreach (var package in packages.OrderBy(p => p.Name, StringComparer.Ordinal))
                foreach (var source in Roots) Scan(package, source.Root, source.Target, result);
            foreach (var skill in Colliding(result.Where(s => s.IsValid)))
                skill.Error = "Multiple packages provide the same skill name: " + skill.Name;
            return result;
        }

        // Skills that share a name and would be exported to at least one common target.
        internal static HashSet<PackageSkill> Colliding(IEnumerable<PackageSkill> skills)
        {
            var result = new HashSet<PackageSkill>();
            foreach (var group in skills.GroupBy(s => s.Name).Where(g => g.Count() > 1))
                foreach (var skill in group)
                    if (group.Any(other => other != skill && (other.Applies(SkillTarget.Codex) && skill.Applies(SkillTarget.Codex) ||
                                                                 other.Applies(SkillTarget.Claude) && skill.Applies(SkillTarget.Claude))))
                        result.Add(skill);
            return result;
        }

        private static void Scan(SkillPackage package, string relativeRoot, SkillTarget? only, List<PackageSkill> result)
        {
            var root = Path.Combine(package.ResolvedPath, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root)) return;
            try
            {
                var parent = Path.GetDirectoryName(root);
                if (parent != package.ResolvedPath) SkillFiles.RejectLink(parent);
                SkillFiles.RejectLink(root);
                foreach (var directory in Directory.GetDirectories(root).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var skill = new PackageSkill { Package = package, Name = Path.GetFileName(directory), SourcePath = directory, OnlyTarget = only };
                    try
                    {
                        skill.Files = SkillFiles.ReadBundle(directory);
                        if (!skill.Files.TryGetValue("SKILL.md", out var manifest))
                            throw new InvalidDataException("SKILL.md is missing.");
                        var header = Header(Encoding.UTF8.GetString(manifest));
                        var name = Field(header, "name");
                        var description = Field(header, "description");
                        if (!SkillFiles.ValidName(name) || name != skill.Name)
                            throw new InvalidDataException("The skill name must match its folder and use lowercase letters, digits and hyphens (1–64 characters).");
                        if (string.IsNullOrWhiteSpace(description))
                            throw new InvalidDataException("The skill description is missing.");
                        skill.Description = description;
                        skill.Fingerprint = SkillFiles.Fingerprint(skill.Files);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException)
                    { skill.Error = ex.Message; }
                    result.Add(skill);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Add(new PackageSkill { Package = package, Name = relativeRoot, SourcePath = root, OnlyTarget = only, Error = ex.Message });
            }
        }

        private static string Header(string document)
        {
            document = document.TrimStart('\uFEFF').Replace("\r\n", "\n");
            if (!document.StartsWith("---\n", StringComparison.Ordinal))
                throw new InvalidDataException("SKILL.md must start with YAML frontmatter.");
            var end = Regex.Match(document.Substring(4), @"(?m)^---\s*$");
            if (!end.Success) throw new InvalidDataException("YAML frontmatter is not closed.");
            return document.Substring(4, end.Index);
        }

        private static string Field(string header, string key)
        {
            var lines = header.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(key + ":", StringComparison.Ordinal)) continue;
                var value = lines[i].Substring(key.Length + 1).Trim();
                if (Regex.IsMatch(value, @"^[>|][+-]?\s*$"))
                {
                    var block = new List<string>();
                    for (i++; i < lines.Length && (string.IsNullOrWhiteSpace(lines[i]) || char.IsWhiteSpace(lines[i][0])); i++)
                        block.Add(lines[i].Trim());
                    return string.Join(value[0] == '>' ? " " : "\n", block).Trim();
                }
                if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                    return value.Substring(1, value.Length - 2).Replace("''", "'");
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    return Regex.Unescape(value.Substring(1, value.Length - 2));
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                return comment < 0 ? value : value.Substring(0, comment).TrimEnd();
            }
            return null;
        }
    }
}
