using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Achieve.UnitySkillManager.Editor
{
    internal static class SkillFiles
    {
        public static bool ValidName(string name) => name != null &&
            Regex.IsMatch(name, "^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant);

        public static string Under(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
                throw new IOException("Path is outside the managed folder.");
            for (var current = path; current.Length > fullRoot.Length; current = Path.GetDirectoryName(current))
                RejectLink(current);
            RejectLink(fullRoot);
            return path;
        }

        public static void RejectLink(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files or folders cannot be synchronized: " + path);
        }

        public static SortedDictionary<string, byte[]> ReadBundle(string directory)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            Read(directory, directory, files);
            return files;
        }

        private static void Read(string root, string directory, SortedDictionary<string, byte[]> files)
        {
            RejectLink(directory);
            foreach (var path in Directory.GetFiles(directory))
            {
                RejectLink(path);
                var relative = path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                files.Add(relative, File.ReadAllBytes(path));
            }
            foreach (var child in Directory.GetDirectories(directory)) Read(root, child, files);
        }

        public static string Fingerprint(SortedDictionary<string, byte[]> files)
        {
            using (var sha = SHA256.Create())
            {
                var text = string.Join("\n", files.Select(file => file.Key + "=" +
                    BitConverter.ToString(sha.ComputeHash(file.Value)).Replace("-", "")));
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
            }
        }

        public static void WriteBundle(string directory, SortedDictionary<string, byte[]> files)
        {
            Directory.CreateDirectory(directory);
            foreach (var file in files)
            {
                var destination = Under(directory, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, file.Value);
            }
        }

        public static void AtomicWrite(string path, string text)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
