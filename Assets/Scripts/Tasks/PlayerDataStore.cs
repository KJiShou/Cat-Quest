using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public interface IPlayerDataStore
{
    PlayerData Data { get; }
    bool TrySave(PlayerData candidate, out string error);
}

public sealed class PlayerDataStore : IPlayerDataStore
{
    private const int CurrentVersion = 5;
    [Serializable] private sealed class SaveHeader { public int version; }
    public PlayerData Data { get; private set; }
    public string Path { get; }
    public string LoadNotice { get; private set; }
    public string WriteBlockReason { get; private set; }
    public PlayerDataStore(string path)
    {
        Path = path;
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
            if (File.Exists(path)) LoadNotice = "The save and backup could not be read. The original files have been kept.";
        }
        Normalize(Data);
    }
    private void BlockNewerSave(int version)
    {
        WriteBlockReason = $"This save or backup was created by a newer version of Cat Quest (save version {version}). Open it with that version or a newer one. Saving is disabled; your files have not been changed.";
        LoadNotice = WriteBlockReason;
    }
    private static bool TryRead(string path, out PlayerData data, out int version)
    {
        data = null;
        version = 0;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path).Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return false;
            // Read only the version first: newer field types may not deserialize here.
            version = JsonUtility.FromJson<SaveHeader>(json)?.version ?? 0;
            if (version > CurrentVersion) return false;
            data = JsonUtility.FromJson<PlayerData>(json);
            if (data == null) return false;
            Normalize(data);
            return true;
        }
        catch (Exception) { return false; }
    }
    public bool TrySave(PlayerData candidate, out string error)
    {
        error = null;
        if (WriteBlockReason != null) { error = WriteBlockReason; return false; }
        string temporary = Path + ".tmp";
        try
        {
            bool valid = TryRead(Path, out _, out int primaryVersion);
            TryRead(Path + ".bak", out _, out int backupVersion);
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
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            // Preserve the pre-tutorial save once, in addition to the rolling atomic backup.
            if (valid && candidate.tutorial != null && Data.tutorial == null && !File.Exists(Path + ".before-tutorial.bak"))
                File.Copy(Path, Path + ".before-tutorial.bak");
            File.WriteAllText(temporary, JsonUtility.ToJson(candidate, true));
            // A corrupt primary must never replace the last valid backup.
            if (File.Exists(Path))
            {
                if (!valid) File.Copy(Path, Path + ".corrupt." + Guid.NewGuid().ToString("N"));
                File.Replace(temporary, Path, valid ? Path + ".bak" : null);
            }
            else File.Move(temporary, Path);
            Data = candidate;
            return true;
        }
        catch (Exception e) { error = "Could not save your changes. " + e.Message; return false; }
    }
    public static T Copy<T>(T value)
    {
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
