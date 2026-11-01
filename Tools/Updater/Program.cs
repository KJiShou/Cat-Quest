using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using CatQuest.Updates;

namespace CatQuestUpdater
{
    public sealed class JournalEntry { public string path; public bool existed; }
    public sealed class Journal { public string state; public List<JournalEntry> entries = new List<JournalEntry>(); }
    public static class Installer
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static T Read<T>(string path) { return Json.Deserialize<T>(File.ReadAllText(path)); }
        public static void Write(string path, object value)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, Json.Serialize(value));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static PackageInventory LoadInventory(string directory)
        {
            var inventory = Read<PackageInventory>(UpdateRules.SafePath(directory, UpdateRules.InventoryName));
            UpdateRules.ValidateInventory(inventory); return inventory;
        }
        public static void Extract(InstallRequest request, string stage)
        {
            if (new FileInfo(request.package).Length != request.size || !string.Equals(UpdateRules.Hash(request.package), request.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package checksum does not match.");
            Directory.CreateDirectory(stage);
            using (var zip = ZipFile.OpenRead(request.package))
            {
                if (zip.Entries.Count > 50001) throw new InvalidDataException("Too many archive entries.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    string path = UpdateRules.SafePath(stage, entry.FullName);
                    if (!seen.Add(entry.FullName) || entry.Length > 8L * 1024 * 1024 * 1024 || (expanded += entry.Length) > 16L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Invalid or oversized archive.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path);
                }
                var inventory = LoadInventory(stage);
                if (UpdateRules.VersionOf(inventory.version) != UpdateRules.VersionOf(request.version) || seen.Count != inventory.files.Length + 1)
                    throw new InvalidDataException("Archive does not match its inventory.");
                foreach (var file in inventory.files)
                {
                    string path = UpdateRules.SafePath(stage, file.path);
                    if (!seen.Contains(file.path) || new FileInfo(path).Length != file.size || !string.Equals(UpdateRules.Hash(path), file.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid file: " + file.path);
                }
            }
        }
        // Each original is backed up before its journal entry is committed, and each entry is
        // committed before touching the destination. Recovery is repeatable after process/power loss.
        public static void Apply(string target, string stage, string transaction, Action<int> afterFile = null)
        {
            var old = LoadInventory(target); var next = LoadInventory(stage);
            if (!UpdateRules.IsNewer(next.version, old.version)) throw new InvalidDataException("Update is not newer than the installed package.");
            Directory.CreateDirectory(transaction);
            string journalPath = Path.Combine(transaction, "journal.json");
            if (File.Exists(journalPath)) throw new IOException("A previous update needs recovery first.");
            var previous = new HashSet<string>(old.files.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
            var paths = old.files.Select(f => f.path).Union(next.files.Select(f => f.path), StringComparer.OrdinalIgnoreCase).Concat(new[] { UpdateRules.InventoryName }).ToArray();
            foreach (string relative in paths)
            {
                string destination = UpdateRules.SafePath(target, relative);
                if (relative != UpdateRules.InventoryName && File.Exists(destination) && !previous.Contains(relative))
                    throw new IOException("A user file conflicts with the update: " + relative);
            }
            var journal = new Journal { state = "installing" };
            Write(journalPath, journal);
            try
            {
                int count = 0;
                foreach (string relative in paths)
                {
                    string destination = UpdateRules.SafePath(target, relative);
                    string backup = UpdateRules.SafePath(Path.Combine(transaction, "backup"), relative);
                    bool existed = File.Exists(destination);
                    if (existed) { Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(destination, backup); }
                    journal.entries.Add(new JournalEntry { path = relative, existed = existed }); Write(journalPath, journal);
                    string source = UpdateRules.SafePath(stage, relative);
                    if (File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(source, destination, true); }
                    else if (existed) File.Delete(destination);
                    if (afterFile != null) afterFile(++count);
                }
                journal.state = "installed"; Write(journalPath, journal);
            }
            catch { Recover(target, transaction); throw; }
        }
        public static void Recover(string target, string transaction)
        {
            string path = Path.Combine(transaction, "journal.json");
            if (!File.Exists(path)) return;
            var journal = Read<Journal>(path);
            if (journal.state != "installing") return;
            foreach (var entry in journal.entries.AsEnumerable().Reverse())
            {
                string destination = UpdateRules.SafePath(target, entry.path);
                if (entry.existed) File.Copy(UpdateRules.SafePath(Path.Combine(transaction, "backup"), entry.path), destination, true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            journal.state = "rolled-back"; Write(path, journal);
        }
    }
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            string result = null;
            try
            {
                if (args.Length == 2 && args[0] == "--recover")
                {
                    string recoveryTarget = Path.GetFullPath(args[1]);
                    using (var recoveryLock = new FileStream(UpdateRules.SafePath(recoveryTarget, UpdateRules.LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        foreach (var running in Process.GetProcessesByName("CatQuest")) using (running)
                            if (!running.HasExited && string.Equals(running.MainModule.FileName, Path.Combine(recoveryTarget,"CatQuest.exe"), StringComparison.OrdinalIgnoreCase))
                                throw new IOException("Close Cat Quest and its Companion before recovery.");
                        string history = recoveryTarget.TrimEnd('\\') + ".update-history";
                        foreach (string prior in Directory.GetDirectories(history)) Installer.Recover(recoveryTarget, prior);
                    }
                    System.Windows.Forms.MessageBox.Show("Interrupted installation recovered. You can reopen Cat Quest.", "Cat Quest Update");
                    return 0;
                }
                if (args.Length != 1) throw new ArgumentException("Expected an update request file.");
                string requestPath = Path.GetFullPath(args[0]);
                var request = Installer.Read<InstallRequest>(requestPath);
                string work = Path.GetDirectoryName(requestPath);
                result = Path.Combine(work, "result.txt");
                string target = Path.GetFullPath(request.directory);
                if (!string.Equals(Path.GetFullPath(request.package), Path.Combine(work, "package.zip"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid package location.");
                if (work.StartsWith(target.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Updater must run outside the installation.");
                string exe = UpdateRules.SafePath(target, "CatQuest.exe");
                // Validate identity before accepting the handoff. Never terminate unrelated processes.
                using (var parent = Process.GetProcessById(request.parentPid))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != request.parentStartedUtcTicks || !string.Equals(parent.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The update owner does not match this installation.");
                    File.WriteAllText(Path.Combine(work, "ready"), "ready");
                    if (!parent.WaitForExit(45000)) throw new IOException("Cat Quest did not exit; no update was installed.");
                }
                using (var installLock = new FileStream(UpdateRules.SafePath(target, UpdateRules.LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (var process in Process.GetProcessesByName("CatQuest"))
                    {
                        using (process)
                        {
                            if (!process.HasExited && string.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase))
                                throw new IOException("Another Cat Quest or Companion is still running. Close it and retry.");
                        }
                    }
                    // Transactions live beside the installation, never in the player's save directory.
                    string transactionRoot = target.TrimEnd('\\') + ".update-history";
                    Directory.CreateDirectory(transactionRoot);
                    foreach (string prior in Directory.GetDirectories(transactionRoot)) Installer.Recover(target, prior);
                    string transaction = Path.Combine(transactionRoot, Guid.NewGuid().ToString("N"));
                    string stage = Path.Combine(work, "stage-" + Guid.NewGuid().ToString("N"));
                    Installer.Extract(request, stage);
                    Installer.Apply(target, stage, transaction);
                    File.WriteAllText(result, "Installed " + request.version + ". Backup: " + transaction);
                }
                string arguments = "";
                if (!string.IsNullOrEmpty(request.verificationDirectory))
                {
                    string isolated = Path.GetFullPath(request.verificationDirectory);
                    if (isolated.Contains("\"")) throw new InvalidDataException("Invalid verification directory.");
                    arguments = "--focus-smoke --smoke-dir \"" + isolated.TrimEnd('\\') + "\" -screen-fullscreen 0 -force-d3d11 -logFile \"" + Path.Combine(isolated,"RestartedPlayer.log") + "\"";
                }
                Process.Start(new ProcessStartInfo(exe, arguments) { WorkingDirectory = target, UseShellExecute = false });
                return 0;
            }
            catch (Exception error)
            {
                if (result != null) try { File.WriteAllText(result, "Update failed: " + error); } catch { }
                System.Windows.Forms.MessageBox.Show("Cat Quest could not complete the update.\n\n" + error.Message + "\n\nYour save was not changed. " + (result == null ? "" : "Details: " + result), "Cat Quest Update", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
