using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class TaskService
{
    // Do not transfer account ownership, publication consent or pending network actions.
    public void TransferBackup(string path, bool export)
    {
        if (!(store is PlayerDataStore disk) || disk.WriteBlockReason != null)
            throw new IOException("The local save is unavailable for backup transfer.");
        string fullPath = Path.GetFullPath(path);
        string live = Path.GetFullPath(disk.Path);
        if (fullPath.Equals(live, StringComparison.OrdinalIgnoreCase) || export && fullPath.StartsWith(live + ".", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a file outside the active save and its recovery files.");
        if (ActiveSession(store.Data)) throw new IOException("Finish your Focus session or battle first.");
        if (export)
        {
            var backup = Snapshot;
            backup.account = new AccountPreferences { playerName = backup.username };
            string json = JsonUtility.ToJson(backup, true);
            if (json.Length > ChatGptJson.MaximumDocumentCharacters) throw new IOException("This save exceeds the backup size limit.");
            WriteBackup(fullPath, json);
            return;
        }
        if (new FileInfo(fullPath).Length > 4L * ChatGptJson.MaximumDocumentCharacters)
            throw new IOException("This backup exceeds the size limit.");
        string input = File.ReadAllText(fullPath);
        var fields = ChatGptJson.Object(input);
        long version = ChatGptJson.Number(fields, "version");
        bool HasList(string key) => fields.TryGetValue(key, out var value) && value is List<object>;
        bool HasBalance(string key) => fields.TryGetValue(key, out var value) && value is long amount && amount >= 0 && amount <= int.MaxValue;
        if (version < 1 || version > disk.Data.version || !new[] { "tasks", "occurrences", "history", "tags" }.All(HasList)
            || !new[] { "fishCoins", "experience", "tickets" }.All(HasBalance)
            || version == disk.Data.version && !new[] { "rewardDays", "focusSessions", "battles", "bossProgress", "equipment", "equipmentUpgrades" }.All(HasList))
            throw new IOException("Choose a supported Cat Quest player backup.");
        var candidate = JsonUtility.FromJson<PlayerData>(input);
        bool InvalidTask(QuestTask t) => t == null || string.IsNullOrWhiteSpace(t.id) || string.IsNullOrWhiteSpace(t.title)
            || t.subtasks?.Any(sub => sub == null) == true || !Enum.IsDefined(typeof(TaskDifficulty), t.difficulty)
            || ((t.scheduled || t.daily) && !TryDate(t.date, out _))
            || (t.daily && !TaskRepeat.ValidRule(t));
        if (candidate.tasks.Any(InvalidTask) || candidate.tasks.GroupBy(t => t.id).Any(g => g.Count() > 1)
            || candidate.tags.Any(t => t == null || string.IsNullOrWhiteSpace(t.id)) || candidate.tags.GroupBy(t => t.id).Any(g => g.Count() > 1)
            || candidate.occurrences.Any(o => o == null || string.IsNullOrEmpty(o.taskId) || (o.day != "once" && !TryDate(o.day, out _)))
            || candidate.occurrences.GroupBy(o => (o.taskId, o.day)).Any(g => g.Count() > 1)
            || candidate.history.Any(h => h == null || InvalidTask(h.snapshot) || h.snapshot.subtasks == null)
            || candidate.history.GroupBy(h => h.id).Any(g => g.Count() > 1)
            || candidate.bossProgress?.Any(p => p == null || p.bossId < 1 || p.bossId > 3) == true
            || candidate.bossProgress?.GroupBy(p => p.bossId).Any(g => g.Count() > 1) == true)
            throw new IOException("The backup contains invalid task or progress records.");
        PlayerDataStore.Normalize(candidate);
        if (ActiveSession(candidate)) throw new IOException("Choose a backup without an unfinished Focus session or battle.");
        candidate.account = PlayerDataStore.Copy(store.Data.account);
        candidate.account.playerName = candidate.username;
        // Keep this recovery file independent of the store's rolling .bak.
        WriteBackup(live + ".before-import-" + Guid.NewGuid().ToString("N") + ".json", JsonUtility.ToJson(store.Data, true));
        foreach (var task in candidate.tasks.Where(t => t.daily && Eligible(t))) GetOccurrence(candidate, task, Today);
        if (!store.TrySave(candidate, out var error)) throw new IOException(error);
        observedDay = Today;
        Changed?.Invoke();
    }

    static bool ActiveSession(PlayerData data) => data.focusSessions.Any(s => s.status == FocusSessionStatus.Running)
        || data.battles.Any(b => b.outcome == BattleOutcome.Running);

    static void WriteBackup(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
