using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public enum PlayerStorageError { None, InvalidData, Busy, DiskFull, AccessDenied, Corrupt, NewerVersion, Migration, Closed, Storage }

public sealed class OperationResult<T>
{
    public bool Succeeded { get; internal set; }
    public T Value { get; internal set; }
    public PlayerStorageError Error { get; internal set; }
    public string Message { get; internal set; }
    public long Version { get; internal set; }
    // Worker time includes SQL/serialization; it is not a pure physical-disk timer.
    public double WorkerMilliseconds { get; internal set; }
    public double QueueWaitMilliseconds { get; internal set; }
    public IReadOnlyCollection<PlayerRecordKind> Changed { get; internal set; } = Array.Empty<PlayerRecordKind>();
}

// The collection is a closed whitelist: callers cannot supply SQL table names.
public enum PlayerRecordKind
{
    Profile, Settings, Account, Tutorial, Tasks, Tags, Occurrences, History,
    RewardDays, FocusSessions, Equipment, EquipmentUpgrades, LootReceipts, Battles, BossProgress
}

public sealed class PlayerRecordQuery
{
    public PlayerRecordKind Kind;
    public string Id, ParentId, DayFrom, DayThrough;
    public int? State;
    public long AfterSequence = -1;
    public int Limit = 100;
}

public interface IPlayerTransaction
{
    T Read<T>(PlayerRecordKind kind, string id) where T : class;
    IReadOnlyList<T> Query<T>(PlayerRecordQuery query) where T : class;
    void Put<T>(PlayerRecordKind kind, T record) where T : class;
    void Delete(PlayerRecordKind kind, string id);
    TaskCompletion FindCompletion(string taskId, string occurrenceDay);
    PlayerDayTotals ReadDayTotals(string day);
    IReadOnlyList<QuestTask> ActiveTasks(string tagId = null, bool includeCompleted = false);
    IReadOnlyList<TaskOccurrence> CurrentOccurrences(string today);
    IReadOnlyList<TaskCompletion> CurrentDailyCompletions(string today);
    IReadOnlyList<string> UndoableCompletions(string today, IEnumerable<string> receiptIds);
    IReadOnlyList<EquipmentItem> Equipment();
    IReadOnlyList<QuestTag> Tags();
}

public sealed class PlayerDayTotals
{
    public long TaskExperience, TaskFishCoins, TaskTickets, RewardReceiptCount, CompletedTasks;
    public double FocusSeconds;
}

public sealed class PlayerCommandException : Exception
{
    public PlayerCommandException(string message) : base(message) { }
}

public sealed class PlayerHistoryPage
{
    public List<TaskView> Rows = new List<TaskView>();
    public string NextCursor;
    public long TotalCount;
}

public sealed class PlayerCalendarPage
{
    public List<CalendarTaskItem> Rows = new List<CalendarTaskItem>();
    public string NextCursor;
    public long TotalCount;
}

public interface IPlayerRepository
{
    string DatabasePath { get; }
    Task<OperationResult<string>> InitializeAsync();
    Task<OperationResult<IReadOnlyList<T>>> QueryAsync<T>(PlayerRecordQuery query) where T : class;
    Task<OperationResult<T>> ExecuteAsync<T>(Func<IPlayerTransaction, T> command);
    Task<OperationResult<T>> ReadAsync<T>(Func<IPlayerTransaction, T> query);
    Task<OperationResult<PlayerHistoryPage>> QueryHistoryPageAsync(string cursor, string search, TaskDifficulty? difficulty, string[] tags);
    Task<OperationResult<PlayerData>> QueryCalendarDataAsync(DateTime first, DateTime last, string today);
    Task<OperationResult<PlayerCalendarPage>> QueryNoDatePageAsync(string cursor, string search, TaskDifficulty? difficulty, string[] tags, bool showCompleted);
    Task<OperationResult<FocusStatistics.Result>> QueryFocusStatisticsAsync(DateTime date, FocusStatisticsPeriod period, TimeZoneInfo zone);
    Task<OperationResult<List<QuestTag>>> QueryFocusStatisticsTagCatalogAsync();
    Task<OperationResult<AccountAggregate>> QueryAccountAggregateAsync(string uid, string name);
    Task<OperationResult<string>> ExportBackupAsync(string destination);
    Task<OperationResult<bool>> ImportBackupAsync(string source);
    Task<OperationResult<bool>> FlushAsync();
    Task<OperationResult<bool>> MaintainAsync();
    Task CloseAsync();
    Task<OperationResult<string>> ReopenAsync();
}
