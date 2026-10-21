using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum LeaderboardKind { BossStage, TasksCompleted, FocusMinutes, Power }

public sealed class LeaderboardSnapshot
{
    public LeaderboardKind kind;
    public readonly List<AccountAggregate> leaders = new List<AccountAggregate>();
    public AccountAggregate own;
    public long ownRank;
    public string readTime, message, ownMessage;
    public bool success;
}

// Independent deterministic ordering per category; never sorts by local unsent progress.
public static class LeaderboardQuery
{
    public const int Limit = 50;
    public static string Title(LeaderboardKind kind) => kind switch {
        LeaderboardKind.BossStage => "Boss Stage", LeaderboardKind.TasksCompleted => "Tasks Completed",
        LeaderboardKind.FocusMinutes => "Focus Minutes", _ => "Power" };
    public static string Field(LeaderboardKind kind) => kind switch {
        LeaderboardKind.BossStage => "bossStage", LeaderboardKind.TasksCompleted => "totalTasksCompleted",
        LeaderboardKind.FocusMinutes => "totalFocusMinutes", _ => "power" };
    public static long Metric(AccountAggregate row, LeaderboardKind kind) => kind switch {
        LeaderboardKind.BossStage => row.bossStage, LeaderboardKind.TasksCompleted => row.totalTasksCompleted,
        LeaderboardKind.FocusMinutes => row.totalFocusMinutes, _ => row.power };
    public static int Compare(AccountAggregate a, AccountAggregate b, LeaderboardKind kind)
    {
        int order = Metric(b, kind).CompareTo(Metric(a, kind));
        if (order == 0 && kind == LeaderboardKind.Power) order = b.catLevel.CompareTo(a.catLevel);
        return order != 0 ? order : string.CompareOrdinal(a.playerId, b.playerId);
    }
    public static string Display(AccountAggregate row, LeaderboardKind kind) => kind == LeaderboardKind.BossStage
        ? row.bossStage <= 3 ? "Boss " + row.bossStage : "Master " + (row.bossStage - 3)
        : Metric(row, kind).ToString("N0", CultureInfo.InvariantCulture);
    static string Order(string field, string direction) => "{\"field\":{\"fieldPath\":" + AccountRest.Quote(field) + "},\"direction\":" + AccountRest.Quote(direction) + "}";
    static string Base(LeaderboardKind kind) => "{\"from\":[{\"collectionId\":\"leaderboard\"}],\"orderBy\":[" + Order(Field(kind), "DESCENDING") +
        (kind == LeaderboardKind.Power ? "," + Order("catLevel", "DESCENDING") : "") + "," + Order("playerId", "ASCENDING") + "]";
    static string Time(string time) => string.IsNullOrEmpty(time) ? "" : ",\"readTime\":" + AccountRest.Quote(time);
    static string Number(long value) => "{\"integerValue\":" + AccountRest.Quote(value.ToString(CultureInfo.InvariantCulture)) + "}";
    public static string Top(LeaderboardKind kind) => "{\"structuredQuery\":" + Base(kind) + ",\"limit\":" + Limit + "}}";
    public static string Own(string uid, string time) => "{\"structuredQuery\":{\"from\":[{\"collectionId\":\"leaderboard\"}],\"where\":{\"fieldFilter\":{\"field\":{\"fieldPath\":\"playerId\"},\"op\":\"EQUAL\",\"value\":{\"stringValue\":" + AccountRest.Quote(uid) + "}}},\"limit\":1}" + Time(time) + "}";
    public static string Rank(LeaderboardKind kind, AccountAggregate own, string time) =>
        "{\"structuredAggregationQuery\":{\"structuredQuery\":" + Base(kind) + ",\"endAt\":{\"before\":true,\"values\":[" + Number(Metric(own, kind)) +
        (kind == LeaderboardKind.Power ? "," + Number(own.catLevel) : "") + ",{\"stringValue\":" + AccountRest.Quote(own.playerId) + "}]}},\"aggregations\":[{\"alias\":\"ahead\",\"count\":{}}]}" + Time(time) + "}";
    public static string Url(string project, bool count = false) => "https://firestore.googleapis.com/v1/projects/" + project + "/databases/(default)/documents:" + (count ? "runAggregationQuery" : "runQuery");
    [Serializable] sealed class Error { public int code; }
    [Serializable] sealed class QueryRow { public AccountRest.Document document; public string readTime; public Error error; }
    [Serializable] sealed class QueryRows { public QueryRow[] items; }
    [Serializable] sealed class CountFields { public AccountRest.Value ahead; }
    [Serializable] sealed class CountResult { public CountFields aggregateFields; }
    [Serializable] sealed class CountRow { public CountResult result; public Error error; }
    [Serializable] sealed class CountRows { public CountRow[] items; }
    public static bool ParseRows(string json, string project, out List<AccountAggregate> rows, out string time)
    {
        rows = new List<AccountAggregate>(); time = null;
        var response = AccountRest.Parse<QueryRows>("{\"items\":" + json + "}");
        if (response?.items == null) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in response.items)
        {
            if (entry == null || entry.error != null && entry.error.code != 0) return false;
            if (!string.IsNullOrEmpty(entry.readTime)) time = entry.readTime;
            if (entry.document == null || string.IsNullOrEmpty(entry.document.name)) continue;
            string uid = entry.document.fields?.playerId?.stringValue;
            if (string.IsNullOrEmpty(uid) || !ids.Add(uid) || entry.document.name != AccountRest.DocumentPath(project, uid) ||
                !AccountRest.TryRemote(JsonUtility.ToJson(entry.document), uid, out var remote)) return false;
            rows.Add(remote.aggregate);
        }
        return DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
    }
    public static bool ParseRank(string json, out long rank)
    {
        rank = 0;
        var response = AccountRest.Parse<CountRows>("{\"items\":" + json + "}");
        if (response?.items == null) return false;
        bool found = false;
        foreach (var item in response.items)
        {
            if (item == null || item.error != null && item.error.code != 0) return false;
            if (item.result == null || string.IsNullOrEmpty(item.result.aggregateFields?.ahead?.integerValue)) continue;
            if (found || !long.TryParse(item.result.aggregateFields?.ahead?.integerValue, NumberStyles.None, CultureInfo.InvariantCulture, out long count) || count < 0 || count == long.MaxValue) return false;
            rank = count + 1; found = true;
        }
        return found;
    }
}
