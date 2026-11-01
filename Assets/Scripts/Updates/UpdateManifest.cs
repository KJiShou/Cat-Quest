using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CatQuest.Updates
{
    // Shared with the standalone .NET Framework updater; keep this file Unity-independent.
    [Serializable] public sealed class ReleaseManifest
    {
        public int format = 1;
        public string version, notes, url, sha256;
        public long size;
    }
    [Serializable] public sealed class PackageFile { public string path, sha256; public long size; }
    [Serializable] public sealed class PackageInventory
    {
        public int format = 1;
        public string version;
        public PackageFile[] files;
    }
    [Serializable] public sealed class InstallRequest
    {
        public string directory, package, result, version, sha256, verificationDirectory;
        public long size, parentStartedUtcTicks;
        public int parentPid;
    }
    public static class UpdateRules
    {
        public const string Repository = "https://github.com/KJiShou/Cat-Quest";
        public const string Feed = Repository + "/releases/latest/download/catquest-update.json";
        public const string InventoryName = "catquest-package.json";
        public const string LockName = ".catquest-update.lock";
        public static Version VersionOf(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"^\d+\.\d+(\.\d+){0,2}$")) throw new InvalidDataException("Invalid release version.");
            var v = new Version(value);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }
        public static bool IsNewer(string release, string installed) { return VersionOf(release) > VersionOf(installed); }
        public static void Validate(ReleaseManifest manifest)
        {
            if (manifest == null || manifest.format != 1) throw new InvalidDataException("Unsupported update manifest.");
            VersionOf(manifest.version);
            Uri uri;
            if (!Uri.TryCreate(manifest.url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
                !manifest.url.StartsWith(Repository + "/releases/download/", StringComparison.Ordinal) ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new InvalidDataException("Update must come from the Cat-Quest GitHub Releases repository.");
            if (!ValidHash(manifest.sha256) || manifest.size <= 0 || manifest.size > 8L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Invalid update size or checksum.");
            if (manifest.notes != null && manifest.notes.Length > 8000) throw new InvalidDataException("Release notes are too long.");
        }
        public static bool ValidHash(string hash) { return hash != null && Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"); }
        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static string SafePath(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > 220 || relative.Contains("\\") || relative.Contains(":") || relative.StartsWith("/"))
                throw new InvalidDataException("Invalid package path.");
            foreach (string part in relative.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Unsafe package path: " + relative);
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path leaves installation.");
            // Do not traverse junctions/symlinks in either the installation or staging tree.
            for (string current = full; current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Update paths cannot contain symbolic links or junctions.");
            return full;
        }
        public static void ValidateInventory(PackageInventory inventory)
        {
            if (inventory == null || inventory.format != 1 || inventory.files == null || inventory.files.Length == 0 || inventory.files.Length > 50000)
                throw new InvalidDataException("Invalid package inventory.");
            VersionOf(inventory.version);
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in inventory.files)
            {
                if (file == null || !seen.Add(file.path) || !ValidHash(file.sha256) || file.size < 0 ||
                    file.path == InventoryName || file.path.StartsWith(".") || file.path.IndexOf("catquest-data", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException("Invalid or duplicate package entry.");
                SafePath(Path.GetTempPath(), file.path);
            }
            if (!seen.Contains("CatQuest.exe") || !seen.Contains("CatQuestUpdater.exe") || !seen.Contains("UnityPlayer.dll"))
                throw new InvalidDataException("Package is missing required application files.");
        }
    }
}
