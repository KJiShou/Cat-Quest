using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

// Deliberately separate from PlayerData: this is the entire upload allowlist.
[Serializable]
public sealed class AccountAggregate
{
    public string playerId, playerName;
    public int bossStage, power, catLevel;
    public long totalTasksCompleted, totalFocusMinutes;

    public static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim() == name &&
        name.Length <= 20 && !name.Any(c => char.IsControl(c) || c == '<' || c == '>');

    public static AccountAggregate From(PlayerData data, string uid, string name)
    {
        if (data == null || string.IsNullOrEmpty(uid) || !ValidName(name))
            throw new ArgumentException("Choose a valid account and display name.");
        TaskService.CatLevel(data.experience, out int level, out _, out _);
        int Wins(int boss) => data.bossProgress.Where(p => p != null && p.bossId == boss).Select(p => p.wins).DefaultIfEmpty().Max();
        int stage = Wins(3) > 0 ? checked(3 + Math.Max(1, data.masterStage)) :
            Wins(2) > 0 && level >= 20 ? 3 : Wins(1) > 0 && level >= 10 ? 2 : 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        double seconds = 0;
        foreach (var session in data.focusSessions)
        {
            if (session == null || string.IsNullOrEmpty(session.id) ||
                (session.status != FocusSessionStatus.Completed && session.status != FocusSessionStatus.EndedEarly) ||
                !seen.Add(session.id)) continue;
            if (double.IsNaN(session.eligibleSeconds) || double.IsInfinity(session.eligibleSeconds) || session.eligibleSeconds < 0)
                throw new ArgumentException("A Focus receipt has an invalid duration. Statistics were not uploaded.");
            double eligible = session.eligibleSeconds > 0 ? session.eligibleSeconds : Math.Max(0, session.eligibleMinutes) * 60d;
            if (double.IsNaN(eligible) || double.IsInfinity(eligible) || eligible < 0)
                throw new ArgumentException("A Focus receipt has an invalid duration. Statistics were not uploaded.");
            seconds += eligible;
        }
        if (double.IsInfinity(seconds) || seconds / 60d >= long.MaxValue)
            throw new ArgumentException("Focus statistics exceed the supported range.");
        return new AccountAggregate
        {
            playerId = uid, playerName = name, bossStage = stage, catLevel = level,
            power = EquipmentRules.Calculate(data).Final.Power,
            totalTasksCompleted = data.history.Where(h => h != null && !h.revoked && !string.IsNullOrEmpty(h.taskId) && !string.IsNullOrEmpty(h.day))
                .Select(h => (h.taskId, h.day)).Distinct().LongCount(),
            totalFocusMinutes = (long)Math.Floor(seconds / 60d)
        };
    }

    public bool SameAs(AccountAggregate other) => other != null && playerId == other.playerId && playerName == other.playerName &&
        bossStage == other.bossStage && power == other.power && catLevel == other.catLevel &&
        totalTasksCompleted == other.totalTasksCompleted && totalFocusMinutes == other.totalFocusMinutes;

    public string Summary => $"Stage {bossStage} · {totalTasksCompleted:N0} tasks · {totalFocusMinutes:N0} Focus min · Power {power:N0} · Lv. {catLevel}";
    public static AccountAggregate ReadPending(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<AccountAggregate>(json); }
        catch (ArgumentException) { return null; }
    }
}

public enum AccountPublishDecision { Create, Update, AcknowledgePending, Conflict }

public static class AccountPublishPolicy
{
    public static AccountPublishDecision Decide(AccountPreferences preferences, AccountRemoteSnapshot remote)
    {
        if (remote == null) throw new ArgumentNullException(nameof(remote));
        if (!remote.exists)
            return string.IsNullOrEmpty(preferences.lastServerUpdateTime) ? AccountPublishDecision.Create : AccountPublishDecision.Conflict;
        if (remote.aggregate == null || remote.aggregate.playerId != preferences.boundUid || string.IsNullOrEmpty(remote.updateTime))
            return AccountPublishDecision.Conflict;
        var pending = AccountAggregate.ReadPending(preferences.pendingSnapshotJson);
        if (pending != null && pending.SameAs(remote.aggregate)) return AccountPublishDecision.AcknowledgePending;
        return !string.IsNullOrEmpty(preferences.lastServerUpdateTime) && preferences.lastServerUpdateTime == remote.updateTime
            ? AccountPublishDecision.Update : AccountPublishDecision.Conflict;
    }
}

public sealed class AccountRemoteSnapshot
{
    public bool exists;
    public string updateTime;
    public AccountAggregate aggregate;
}
