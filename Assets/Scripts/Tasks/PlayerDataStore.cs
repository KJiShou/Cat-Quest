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
    public PlayerData Data { get; private set; }
    public string Path { get; }
    public string LoadNotice { get; private set; }
    public PlayerDataStore(string path)
    {
        Path = path;
        if (TryRead(path, out var loaded)) Data = loaded;
        else if (TryRead(path + ".bak", out loaded))
        {
            Data = loaded;
            LoadNotice = "Your backup has been recovered because the save could not be read.";
        }
        else
        {
            Data = new PlayerData();
            if (File.Exists(path)) LoadNotice = "The save and backup could not be read. The original files have been kept.";
        }
        Normalize(Data);
    }
    private static bool TryRead(string path, out PlayerData data)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path).Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return false;
            data = JsonUtility.FromJson<PlayerData>(json);
            if (data == null || data.version > 4) return false;
            Normalize(data);
            return true;
        }
        catch (Exception) { return false; }
    }
    public bool TrySave(PlayerData candidate, out string error)
    {
        error = null;
        string temporary = Path + ".tmp";
        try
        {
            Normalize(candidate);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(temporary, JsonUtility.ToJson(candidate, true));
            // A corrupt primary must never replace the last valid backup.
            if (File.Exists(Path))
            {
                bool valid = TryRead(Path, out _);
                if (!valid) File.Copy(Path, Path + ".corrupt." + Guid.NewGuid().ToString("N"));
                File.Replace(temporary, Path, valid ? Path + ".bak" : null);
            }
            else File.Move(temporary, Path);
            Data = candidate;
            return true;
        }
        catch (Exception e) { error = "Could not save your changes. " + e.Message; return false; }
    }
    public static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    public static void Normalize(PlayerData data)
    {
        data.version = 4;
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
        foreach (var session in data.focusSessions)
        {
            if (session.focusDurationSeconds <= 0) session.focusDurationSeconds = 1500;
            if (session.restDurationSeconds <= 0) session.restDurationSeconds = 300;
        }
        data.tasks.RemoveAll(t => t == null);
        data.tags.RemoveAll(t => t == null);
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
            if (string.IsNullOrEmpty(item.completedLocalDay) && DateTimeOffset.TryParse(item.completedUtc, out var completed))
                item.completedLocalDay = TaskService.DateKey(completed.LocalDateTime);
            if (string.IsNullOrEmpty(item.rewardedLocalDay)) item.rewardedLocalDay = item.completedLocalDay;
        }
    }
}
