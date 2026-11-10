using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public interface IPlayerDataStore
{
    PlayerData Data { get; }
    bool TrySave(PlayerData candidate, out string error);
}

public sealed class PlayerDataStore : IPlayerDataStore
{
    private const int CurrentVersion = 5;
    private const int ParallelWorkMinimumBytes = 128 * 1024;
    [Serializable] private sealed class SaveHeader { public int version; }
    private sealed class ReadValidation
    {
        public byte[] content;
        public long byteLength;
        public int version;
        public bool valid;
    }
    private ReadValidation primaryValidation, backupValidation;
    public PlayerData Data { get; private set; }
    public string Path { get; }
    public string LoadNotice { get; private set; }
    public string WriteBlockReason { get; private set; }
    private readonly BrowserPlayerSave browser;
    public PlayerDataStore(string path, BrowserPlayerSave browserSave = null)
    {
        Path = path; browser = browserSave;
#if UNITY_WEBGL && !UNITY_EDITOR
        try { browser ??= BrowserPlayerSave.Open(); }
        catch (Exception error) {
            WriteBlockReason = LoadNotice = error.Message;
            Data = new PlayerData(); Normalize(Data); return;
        }
#endif
        bool primaryValid = TryRead(path, out var loaded, out int primaryVersion);
        bool backupValid = TryRead(path + ".bak", out var backup, out int backupVersion);
        if (Math.Max(primaryVersion, backupVersion) > CurrentVersion)
        {
            BlockNewerSave(Math.Max(primaryVersion, backupVersion));
            // Do not interpret an unsupported payload or silently roll back to its backup.
            Data = primaryValid ? loaded : new PlayerData();
        }
        else if (primaryValid) Data = loaded;
        else if (backupValid)
        {
            Data = backup;
            LoadNotice = "Your backup has been recovered because the save could not be read.";
        }
        else
        {
            Data = new PlayerData();
            Data.tutorial = new TutorialProgress { version = 1 };
            if (HasPrimary()) LoadNotice = "The save and backup could not be read. The original data has been kept.";
        }
        Normalize(Data);
    }
    private void BlockNewerSave(int version)
    {
        WriteBlockReason = $"This save or backup was created by a newer version of Cat Quest (save version {version}). Open it with that version or a newer one. Saving is disabled; your files have not been changed.";
        LoadNotice = WriteBlockReason;
    }
    private bool HasPrimary()
    {
        return browser != null ? !string.IsNullOrEmpty(browser.Primary) : File.Exists(Path);
    }
    private bool TryRead(string path, out PlayerData data, out int version, bool validateOnly = false)
    {
        data = null;
        version = 0;
        try
        {
            var previous = path == Path ? primaryValidation : backupValidation;
            string json;
            byte[] content;
            long byteLength;
            {
                if (browser != null) {
                    json = path == Path ? browser.Primary : browser.Backup;
                    var bytes = Encoding.UTF8.GetBytes(json ?? "");
                    byteLength = bytes.Length;
                    using (var stream = new MemoryStream(bytes, false)) {
                        if (validateOnly && Matches(previous, stream)) { version = previous.version; return previous.valid; }
                    }
                    content = bytes;
                } else {
                    if (!File.Exists(path)) return false;
                    // Compare every byte, even when timestamps/length are unchanged.
                    // Retain the UTF-8 bytes already read/written. Recompressing
                    // them on every save costs more than the actual file write.
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        byteLength = stream.Length;
                        if (validateOnly && Matches(previous, stream)) {
                            version = previous.version; return previous.valid;
                        }
                        stream.Position = 0;
                        content = new byte[checked((int)stream.Length)];
                        int offset = 0;
                        while (offset < content.Length) {
                            int read = stream.Read(content, offset, content.Length - offset);
                            if (read == 0) throw new EndOfStreamException();
                            offset += read;
                        }
                        // Keep BOM-compatible reads for existing UTF-16 saves.
                        using (var memory = new MemoryStream(content, false))
                        using (var reader = new StreamReader(memory, Encoding.UTF8, true)) json = reader.ReadToEnd();
                    }
                }
            }
            var validation = new ReadValidation { content = content, byteLength = byteLength };
            if (path == Path) primaryValidation = validation; else backupValidation = validation;
            if (string.IsNullOrWhiteSpace(json)) return false;
            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return false;
            // Read only the version first: newer field types may not deserialize here.
            version = JsonUtility.FromJson<SaveHeader>(json)?.version ?? 0;
            validation.version = version;
            if (version > CurrentVersion) return false;
            data = JsonUtility.FromJson<PlayerData>(json);
            if (data == null) return false;
            Normalize(data);
            validation.valid = true;
            return true;
        }
        catch (Exception) { return false; }
    }
    private static bool Matches(ReadValidation previous, Stream current)
    {
        if (previous == null || previous.byteLength != current.Length) return false;
        var left = new byte[8192];
        int offset = 0;
        int count;
        while ((count = current.Read(left, 0, left.Length)) > 0) {
            if (count > previous.content.Length - offset) return false;
            // Compare exact bytes eight at a time; this is not a hash and
            // cannot miss a changed byte. Check the unaligned tail too.
            int aligned = count & ~7;
            var leftWords = MemoryMarshal.Cast<byte, ulong>(left.AsSpan(0, aligned));
            var rightWords = MemoryMarshal.Cast<byte, ulong>(previous.content.AsSpan(offset, aligned));
            for (int i = 0; i < leftWords.Length; i++) if (leftWords[i] != rightWords[i]) return false;
            for (int i = aligned; i < count; i++) if (left[i] != previous.content[offset + i]) return false;
            offset += count;
        }
        return offset == previous.content.Length;
    }
    private static bool MatchesFile(string path, ReadValidation previous)
    {
        if (previous == null) return false;
        try {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Matches(previous, stream);
        } catch (Exception) { return false; }
    }
    private void ValidateFiles(out bool valid, out int primaryVersion, out int backupVersion)
    {
        if (browser != null || primaryValidation == null || backupValidation == null
            || primaryValidation.byteLength < ParallelWorkMinimumBytes || backupValidation.byteLength < ParallelWorkMinimumBytes) {
            valid = TryRead(Path, out _, out primaryVersion, true);
            TryRead(Path + ".bak", out _, out backupVersion, true);
            return;
        }
        // Workers only compare immutable cached bytes with files. Changed content
        // still goes through the existing Unity JSON reader on the calling thread.
        var primary = primaryValidation;
        var backup = backupValidation;
        var compareBackup = Task.Run(() => MatchesFile(Path + ".bak", backup));
        bool primaryMatches = MatchesFile(Path, primary);
        bool backupMatches = compareBackup.GetAwaiter().GetResult();
        if (primaryMatches) { valid = primary.valid; primaryVersion = primary.version; }
        else valid = TryRead(Path, out _, out primaryVersion, true);
        if (backupMatches) backupVersion = backup.version;
        else TryRead(Path + ".bak", out _, out backupVersion, true);
    }
    private void ReplaceFile(string temporary, bool valid, Action<string, string, string> replace = null)
    {
        // The isolated verifier supplies a writer to force precise failure windows.
        replace ??= File.Replace;
        for (int attempt = 0; ; attempt++) {
            try {
                replace(temporary, Path, valid ? Path + ".bak" : null);
                return;
            } catch (IOException) when (attempt < 2 && File.Exists(temporary)) {
                // A brief Windows file lock can reject replacement. Retry only
                // this prepared write, never settlement, and only if both originals
                // remain byte-for-byte unchanged. Persistent failure still escapes.
                System.Threading.Thread.Sleep(2 << attempt);
                if (!MatchesFile(Path, primaryValidation)
                    || (backupValidation == null ? File.Exists(Path + ".bak") : !MatchesFile(Path + ".bak", backupValidation))) throw;
            }
        }
    }
    public bool TrySave(PlayerData candidate, out string error)
    {
        error = null;
        if (WriteBlockReason != null) { error = WriteBlockReason; return false; }
        string temporary = Path + ".tmp";
        try
        {
            ValidateFiles(out bool valid, out int primaryVersion, out int backupVersion);
            int newest = Math.Max(primaryVersion, backupVersion);
            if (newest > CurrentVersion)
            {
                BlockNewerSave(newest); error = WriteBlockReason; return false;
            }
            if (candidate.version > CurrentVersion)
            {
                error = "Cannot save data from a newer version of Cat Quest."; return false;
            }
            Normalize(candidate);
            string json = JsonUtility.ToJson(candidate);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            if (browser != null)
            {
                browser.Commit(json, valid, valid && candidate.tutorial != null && Data.tutorial == null);
            }
            else
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                // Preserve the pre-tutorial save once, in addition to the rolling atomic backup.
                if (valid && candidate.tutorial != null && Data.tutorial == null && !File.Exists(Path + ".before-tutorial.bak"))
                    File.Copy(Path, Path + ".before-tutorial.bak");
                File.WriteAllBytes(temporary, bytes);
                // A corrupt primary must never replace the last valid backup.
                if (File.Exists(Path))
                {
                    if (!valid) File.Copy(Path, Path + ".corrupt." + Guid.NewGuid().ToString("N"));
                    ReplaceFile(temporary, valid);
                }
                else File.Move(temporary, Path);
            }
            // Publish cached validation only after the same atomic write succeeds.
            // Only the last primary/backup bytes are retained, not save strings
            // or extra parsed graphs. A failed attempt never publishes its bytes.
            if (valid) backupValidation = primaryValidation;
            primaryValidation = new ReadValidation { content = bytes, byteLength = bytes.Length, version = CurrentVersion, valid = true };
            Data = candidate;
            return true;
        }
        catch (Exception) { error = "Could not save your changes. Please try again."; return false; }
    }
    public static T Copy<T>(T value)
    {
        if (value is PlayerData player) return (T)(object)player.DetachedCopy();
        var result = JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        // JsonUtility can materialize an empty object for a null nested serializable field.
        if(value is PlayerData original && result is PlayerData copy && original.tutorial == null) copy.tutorial = null;
        return result;
    }
    public static void Normalize(PlayerData data)
    {
        data.version = CurrentVersion;
        data.settings = AppSettings.Normalize(data.settings);
        data.account = AccountPreferences.Normalize(data.account);
        if (data.tutorial != null && data.tutorial.version <= 0) data.tutorial = null;
        if (data.tutorial != null) data.tutorial.read ??= new List<string>();
        data.masterStage = Math.Max(1,data.masterStage);
        data.battles ??= new List<BattleAttempt>();
        data.bossProgress ??= new List<BossProgress>();
        data.battles.RemoveAll(b=>b==null);
        data.bossProgress.RemoveAll(b=>b==null);
        for(int id=1;id<=3;id++) if(!data.bossProgress.Exists(p=>p.bossId==id)) data.bossProgress.Add(new BossProgress {bossId=id});
        data.tasks ??= new List<QuestTask>();
        data.tags ??= new List<QuestTag>();
        data.occurrences ??= new List<TaskOccurrence>();
        data.history ??= new List<TaskCompletion>();
        data.rewardDays ??= new List<RewardDay>();
        data.focusSessions ??= new List<FocusSession>();
        data.rewardDays.RemoveAll(d => d == null);
        data.focusSessions.RemoveAll(s => s == null);
        EquipmentRules.Normalize(data);
        foreach (var session in data.focusSessions)
        {
            if (session.mode != FocusTimerMode.Stopwatch && session.focusDurationSeconds <= 0) session.focusDurationSeconds = 1500;
            if (session.mode == FocusTimerMode.Pomodoro && session.restDurationSeconds <= 0) session.restDurationSeconds = 300;
            if (session.elapsedSeconds <= 0) session.elapsedSeconds = session.eligibleSeconds > 0 ? session.eligibleSeconds : session.eligibleMinutes * 60d;
        }
        data.tasks.RemoveAll(t => t == null);
        data.tags.RemoveAll(t => t == null);
        foreach (var tag in data.tags) tag.colorHex = TagColors.Normalize(tag.colorHex);
        data.occurrences.RemoveAll(t => t == null);
        data.history.RemoveAll(t => t == null || t.snapshot == null);
        foreach (var task in data.tasks)
        {
            task.tagIds ??= new List<string>();
            task.subtasks ??= new List<QuestSubtask>();
        }
        foreach (var item in data.occurrences) item.completedSubtaskIds ??= new List<string>();
        foreach (var item in data.history)
        {
            item.tagNames ??= new List<string>();
            item.tagColors ??= new List<string>();
            if (string.IsNullOrEmpty(item.completedLocalDay) && DateTimeOffset.TryParse(item.completedUtc, out var completed))
                item.completedLocalDay = TaskService.DateKey(completed.LocalDateTime);
            if (string.IsNullOrEmpty(item.rewardedLocalDay)) item.rewardedLocalDay = item.completedLocalDay;
        }
    }
}
