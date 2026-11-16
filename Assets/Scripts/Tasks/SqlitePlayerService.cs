using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Presentation cache contains current tasks, small profile/configuration records
// and a bounded history window. It is never a candidate for persistence.
public sealed partial class SqlitePlayerService
{
    readonly IPlayerRepository repository;
    readonly ITaskClock clock;
    readonly SemaphoreSlim commands = new SemaphoreSlim(1, 1);
    TaskService view;
    PlayerData presentation;
    PlayerDayTotals dayTotals = new PlayerDayTotals();
    sealed class ReceiptIdentity { public string id, taskId, day; public bool revoked; }
    readonly Dictionary<string, ReceiptIdentity> readReceiptLookup = new Dictionary<string, ReceiptIdentity>(StringComparer.Ordinal);
    readonly HashSet<string> visibleReceiptIds = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> visibleReceiptScopes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    long historyReceiptRequest, calendarReceiptRequest;
#if UNITY_EDITOR
    public Func<string, Task> BeforeReceiptRegistrationForVerification;
#endif
    readonly Queue<string> supplementaryReceiptIds = new Queue<string>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public int CachedReceiptIdentitiesForVerification => readReceiptLookup.Count;
#endif
    readonly HashSet<string> undoableReadReceipts = new HashSet<string>(StringComparer.Ordinal);
    long publishedVersion = -1;
    bool initializing, closed;
    bool acceptingCommands = true;
    bool projectionBlocked;
    readonly AsyncLocal<bool> startupCommand = new AsyncLocal<bool>();
    public bool IsReady { get; private set; }
    public string LastError { get; private set; }
    public string StorageVersion { get; private set; }
    public double LastCommandMainMilliseconds { get; private set; }
    public double LastCommandStorageMilliseconds { get; private set; }
    public double LastCommandQueueMilliseconds { get; private set; }
    double lastProjectionMainMilliseconds;
    string projectionDay;
    public event Action Changed;
    public event Action<string> StorageWarning;
    public event Action<TaskService.LevelUpReward> LevelUp;
    public event Action<IReadOnlyCollection<PlayerRecordKind>> RecordsChanged;
    static void Publish(Action listeners)
    {
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList()) {
            try { listener(); }
            catch (Exception) { UnityEngine.Debug.LogError("SQLite presentation callback failed after commit. Refresh the page."); }
        }
    }
    static void Publish<T>(Action<T> listeners, T value)
    {
        if (listeners == null) return;
        foreach (Action<T> listener in listeners.GetInvocationList()) {
            try { listener(value); }
            catch (Exception) { UnityEngine.Debug.LogError("SQLite presentation callback failed after commit. Refresh the page."); }
        }
    }
    sealed class ReadStore : IPlayerDataStore
    {
        public PlayerData Data { get; }
        public ReadStore(PlayerData data) { Data = data; }
        public bool TrySave(PlayerData candidate, out string error) { error = "This is a read-only presentation snapshot."; return false; }
    }
    public SqlitePlayerService(IPlayerRepository repository, ITaskClock clock)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        presentation = new PlayerData(); PlayerDataStore.Normalize(presentation);
        view = new TaskService(new ReadStore(presentation), clock, false);
    }
#if UNITY_EDITOR
    public Task<OperationResult<bool>> ApplyVerificationFixtureAsync(Action<IPlayerTransaction> fixture)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        if (string.IsNullOrEmpty(isolated) || repository.DatabasePath == ":memory:" ||
            !string.Equals(System.IO.Path.GetFullPath(System.IO.Path.ChangeExtension(isolated, ".sqlite3")), repository.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture mutations require the explicitly injected isolated save.");
        return ExecuteCommandAsync(tx => { fixture(tx); return true; });
    }
#endif
    public async Task<OperationResult<bool>> InitializeAsync()
    {
        if (IsReady) return new OperationResult<bool> { Succeeded = true, Value = true, Version = publishedVersion };
        initializing = true;
        startupCommand.Value = true;
        try {
            var opened = await repository.InitializeAsync();
            if (!opened.Succeeded) return InitializationFailure(opened.Error, opened.Message);
            StorageVersion = opened.Value;
            var taskState = await InitializeTaskStateAsync();
            if (!taskState.Succeeded) return taskState;
            var recovered = await RecoverInterruptedBattlesAsync();
            if (!recovered.Succeeded) return recovered;
            var loaded = await RefreshPresentationAsync();
            if (!loaded.Succeeded) return loaded;
            IsReady = true; projectionBlocked = false; LastError = null;
            Publish(RecordsChanged, (IReadOnlyCollection<PlayerRecordKind>)null);
            Publish(Changed);
            return loaded;
        } finally { initializing = false; startupCommand.Value = false; }
    }
    OperationResult<bool> InitializationFailure(PlayerStorageError error, string message)
    { LastError = message; return new OperationResult<bool> { Error = error, Message = message }; }
    async Task<OperationResult<T>> ExecuteCommandAsync<T>(Func<IPlayerTransaction, T> command)
    {
        if (closed || !acceptingCommands || !IsReady && !startupCommand.Value) return new OperationResult<T> { Error = PlayerStorageError.Closed, Message = "Wait for Cat Quest to finish starting or saving." };
        await commands.WaitAsync();
        try {
            if (projectionBlocked) return new OperationResult<T> { Error = PlayerStorageError.Closed, Message = LastError };
            int previousExperience = Experience;
            var mainTimer = System.Diagnostics.Stopwatch.StartNew();
            var pending = repository.ExecuteAsync(command);
            double dispatchMilliseconds = mainTimer.Elapsed.TotalMilliseconds;
            var result = await pending;
            LastCommandStorageMilliseconds = result.WorkerMilliseconds;
            LastCommandQueueMilliseconds = result.QueueWaitMilliseconds;
            mainTimer.Restart();
            LastError = result.Message;
            if (!result.Succeeded) { LastCommandMainMilliseconds = dispatchMilliseconds + mainTimer.Elapsed.TotalMilliseconds; return result; }
            if (result.Value is LootReceipt loot) LastLootReceipt = loot;
            if (!initializing) {
                OperationResult<bool> refreshed;
                try { refreshed = await RefreshPresentationAsync(result.Changed); }
                catch (Exception) { return FreezeAfterCommittedProjectionFailure(result, "Your change was saved"); }
                LastCommandStorageMilliseconds += refreshed.WorkerMilliseconds;
                LastCommandQueueMilliseconds += refreshed.QueueWaitMilliseconds;
                mainTimer.Restart();
                if (!refreshed.Succeeded) return FreezeAfterCommittedProjectionFailure(result, "Your change was saved");
                TaskService.CatLevel(previousExperience, out int from, out _, out _);
                TaskService.CatLevel(Experience, out int to, out _, out _);
                Publish(RecordsChanged, result.Changed);
                Publish(Changed);
                if (to > from) Publish(LevelUp, new TaskService.LevelUpReward(from, to, checked((int)(5L * (to - from) * (from + to + 1L) / 2)), to / 5 - from / 5));
            }
            LastCommandMainMilliseconds = dispatchMilliseconds + lastProjectionMainMilliseconds + mainTimer.Elapsed.TotalMilliseconds;
            return result;
        } finally { commands.Release(); }
    }
    OperationResult<T> FreezeAfterCommittedProjectionFailure<T>(OperationResult<T> result, string saved)
    {
        IsReady = false; acceptingCommands = false; projectionBlocked = true;
        result.Message = saved + ", but the display could not refresh. Restart Cat Quest to load your saved progress. Do not repeat this operation.";
        LastError = result.Message;
        Publish(StorageWarning, LastError);
        return result;
    }
    sealed class Projection { internal PlayerData data; internal PlayerDayTotals totals; }
    async Task<OperationResult<bool>> RefreshPresentationAsync(IReadOnlyCollection<PlayerRecordKind> changed = null)
    {
        string today = TaskService.DateKey(clock.LocalNow);
        var prior = presentation;
        var priorTotals = dayTotals;
        bool full = changed == null || projectionDay != today;
        bool Has(params PlayerRecordKind[] kinds) => full || kinds.Any(changed.Contains);
        bool tasksChanged = Has(PlayerRecordKind.Tasks, PlayerRecordKind.Occurrences);
        bool receiptsChanged = tasksChanged || Has(PlayerRecordKind.History);
        var result = await repository.ReadAsync(tx => {
            var p = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
            p.settings = tx.Read<AppSettings>(PlayerRecordKind.Settings, "settings");
            p.account = tx.Read<AccountPreferences>(PlayerRecordKind.Account, "account");
            p.tutorial = tx.Read<TutorialProgress>(PlayerRecordKind.Tutorial, "tutorial");
            p.tags = Has(PlayerRecordKind.Tags) ? tx.Tags().ToList() : prior.tags;
            p.tasks = tasksChanged ? tx.ActiveTasks().ToList() : prior.tasks;
            p.occurrences = tasksChanged ? tx.CurrentOccurrences(today).ToList() : prior.occurrences;
            p.history = receiptsChanged ? tx.CurrentDailyCompletions(today).ToList() : prior.history;
            p.focusSessions = Has(PlayerRecordKind.FocusSessions) ? tx.Query<FocusSession>(new PlayerRecordQuery { Kind = PlayerRecordKind.FocusSessions, State = (int)FocusSessionStatus.Running }).ToList() : prior.focusSessions;
            p.battles = Has(PlayerRecordKind.Battles) ? tx.Query<BattleAttempt>(new PlayerRecordQuery { Kind = PlayerRecordKind.Battles, State = (int)BattleOutcome.Running }).ToList() : prior.battles;
            p.equipment = Has(PlayerRecordKind.Equipment) ? tx.Equipment().ToList() : prior.equipment;
            p.bossProgress = Has(PlayerRecordKind.BossProgress) ? tx.Query<BossProgress>(new PlayerRecordQuery { Kind = PlayerRecordKind.BossProgress }).ToList() : prior.bossProgress;
            p.lootReceipts = new List<LootReceipt>(); p.equipmentUpgrades = new List<EquipmentUpgradeReceipt>();
            var totals = Has(PlayerRecordKind.History, PlayerRecordKind.FocusSessions, PlayerRecordKind.RewardDays) ? tx.ReadDayTotals(today) : priorTotals;
            var day = tx.Read<RewardDay>(PlayerRecordKind.RewardDays, today) ?? new RewardDay { day = today,
                taskExperience = (int)Math.Min(int.MaxValue, totals.TaskExperience), taskFishCoins = (int)Math.Min(int.MaxValue, totals.TaskFishCoins), taskTickets = checked((int)totals.TaskTickets) };
            if (!day.rewardRulesInitialized) {
                bool had = day.taskExperience > 0 || day.taskFishCoins > 0 || day.questsClaimed || totals.RewardReceiptCount > 0;
                day.firstTaskSettled |= had; day.firstQuestClaimed |= had; day.rewardRulesInitialized = true;
            }
            p.rewardDays = new List<RewardDay> { day };
            return new Projection { data = p, totals = totals };
        });
        if (!result.Succeeded) return new OperationResult<bool> { Error = result.Error, Message = result.Message, WorkerMilliseconds = result.WorkerMilliseconds, QueueWaitMilliseconds = result.QueueWaitMilliseconds };
        var projectionTimer = System.Diagnostics.Stopwatch.StartNew();
        if (result.Version >= publishedVersion) {
            publishedVersion = result.Version; presentation = result.Value.data; dayTotals = result.Value.totals;
            projectionDay = today;
            view = new TaskService(new ReadStore(presentation), clock, false);
            if (receiptsChanged) { ResetReadReceiptCache(); }
        }
        lastProjectionMainMilliseconds = projectionTimer.Elapsed.TotalMilliseconds;
        return new OperationResult<bool> { Succeeded = true, Value = true, Version = result.Version, WorkerMilliseconds = result.WorkerMilliseconds, QueueWaitMilliseconds = result.QueueWaitMilliseconds };
    }
    public async Task<OperationResult<bool>> FlushAsync()
    {
        await commands.WaitAsync();
        try { return await repository.FlushAsync(); }
        finally { commands.Release(); }
    }
    public Task<OperationResult<bool>> PrepareShutdownAsync() { acceptingCommands = false; return FlushAsync(); }
    public void ResumeCommandAcceptance() { if (!closed) acceptingCommands = true; }
    public Task<OperationResult<bool>> MaintainAsync() => repository.MaintainAsync();
    public async Task CloseAsync() { closed = true; await commands.WaitAsync(); try { await repository.CloseAsync(); IsReady = false; } finally { commands.Release(); } }
    public async Task CloseForShutdownAsync(int timeoutMilliseconds = 10000)
    {
        var closingTask = CloseAsync();
        if (await Task.WhenAny(closingTask, Task.Delay(Math.Max(1, timeoutMilliseconds))) == closingTask) { await closingTask; return; }
        LastError = "Storage is still closing. Cat Quest will stay open; please retry after it finishes.";
        _ = RecoverTimedOutCloseAsync(closingTask);
        throw new TimeoutException(LastError);
    }
    async Task RecoverTimedOutCloseAsync(Task closingTask)
    {
        try {
            await closingTask;
            var reopened = await repository.ReopenAsync();
            if (!reopened.Succeeded) { LastError = reopened.Message; return; }
            var refreshed = await RefreshPresentationAsync();
            if (!refreshed.Succeeded) { LastError = refreshed.Message; return; }
            closed = false; acceptingCommands = true; projectionBlocked = false; IsReady = true;
            Publish(RecordsChanged, (IReadOnlyCollection<PlayerRecordKind>)null); Publish(Changed);
        } catch (Exception) { LastError = "Storage could not reopen. Restart Cat Quest to recover your saved progress."; }
    }
    public DateTime LocalNow => clock.LocalNow;
    public int ReadVersion => unchecked((int)publishedVersion);
    public int FishCoins => presentation.fishCoins;
    public int Tickets => presentation.tickets;
    public int Experience => presentation.experience;
    public string PlayerName => presentation.username;
    public List<QuestTag> TagCatalog => view.TagCatalog;
    public AppSettings Settings => view.Settings;
    public AccountPreferences AccountSettings => view.AccountSettings;
    public TutorialProgress Tutorial => view.Tutorial;
    public bool ReduceMotion => view.ReduceMotion;
    public PlayerData EquipmentSnapshot => view.EquipmentSnapshot;
    public PlayerData TaskSnapshot => view.TaskSnapshot;
    public bool EquipmentUnlocked => view.EquipmentUnlocked;
    public EquipmentStats EquipmentStats => view.EquipmentStats;
    public CombatStats CatStats => view.CatStats;
    public int MasterStage => view.MasterStage;
    public bool HasActiveBattle => view.HasActiveBattle;
    public int RewardLevel => view.RewardLevel;
    public int TaskExperienceLimit => view.TaskExperienceLimit;
    public int TaskFishLimit => view.TaskFishLimit;
    public int FocusRewardLevel => view.FocusRewardLevel;
    public int FocusDailyMinuteLimit => view.FocusDailyMinuteLimit;
    public int FocusSessionMinuteLimit => view.FocusSessionMinuteLimit;
    public RewardDay TodayRewards => view.TodayRewards;
    public DailyRewardGoal CurrentTaskGoal => view.CurrentTaskGoal;
    public bool CanAccess(ProgressionFeature feature) => view.CanAccess(feature);
    public string AccessMessage(ProgressionFeature feature) => view.AccessMessage(feature);
    public bool Eligible(QuestTask task) => view.Eligible(task);
    public bool IsOverdue(TaskView task) => view.IsOverdue(task);
    public bool HasIncompleteSubtasks(string id) => view.HasIncompleteSubtasks(id);
    void ResetReadReceiptCache()
    {
        readReceiptLookup.Clear(); undoableReadReceipts.Clear(); visibleReceiptIds.Clear(); visibleReceiptScopes.Clear(); supplementaryReceiptIds.Clear();
        RegisterReadReceipts(presentation.history, null, true);
    }
    void RegisterReadReceipts(IEnumerable<TaskCompletion> receipts, IEnumerable<string> eligible = null, bool visible = false)
    {
        string today = TaskService.DateKey(clock.LocalNow);
        var active = presentation.tasks.ToDictionary(t => t.id, StringComparer.Ordinal);
        var completed = new HashSet<(string,string)>(presentation.occurrences.Where(o=>o.completed).Select(o=>(o.taskId,o.day)));
        foreach (var receipt in receipts) {
            if (receipt == null || string.IsNullOrEmpty(receipt.id)) continue;
            readReceiptLookup[receipt.id] = new ReceiptIdentity { id=receipt.id, taskId=receipt.taskId, day=receipt.day, revoked=receipt.revoked };
            if (visible) visibleReceiptIds.Add(receipt.id);
            else if (!visibleReceiptIds.Contains(receipt.id)) {
                supplementaryReceiptIds.Enqueue(receipt.id);
                while (supplementaryReceiptIds.Count > 256) {
                    string evicted = supplementaryReceiptIds.Dequeue();
                    if (!visibleReceiptIds.Contains(evicted) && !supplementaryReceiptIds.Contains(evicted)) { readReceiptLookup.Remove(evicted); undoableReadReceipts.Remove(evicted); }
                }
            }
            if (eligible == null && !receipt.revoked && active.TryGetValue(receipt.taskId, out var task) && receipt.day == (task.daily ? today : "once")
                && completed.Contains((task.id,receipt.day))) undoableReadReceipts.Add(receipt.id);
        }
        if(eligible!=null)undoableReadReceipts.UnionWith(eligible);
    }
    void SetVisibleReceiptScope(string scope, IEnumerable<TaskCompletion> batch, bool append)
    {
        if (!append || !visibleReceiptScopes.TryGetValue(scope, out var identities))
            visibleReceiptScopes[scope] = identities = new HashSet<string>(StringComparer.Ordinal);
        identities.UnionWith(batch.Select(r=>r.id));
        visibleReceiptIds.Clear();
        visibleReceiptIds.UnionWith(presentation.history.Select(r=>r.id));
        foreach (var activeScope in visibleReceiptScopes.Values) visibleReceiptIds.UnionWith(activeScope);
        var supplementary = new HashSet<string>(supplementaryReceiptIds, StringComparer.Ordinal);
        foreach (string id in readReceiptLookup.Keys.Where(id=>!visibleReceiptIds.Contains(id)&&!supplementary.Contains(id)).ToArray()) {
            readReceiptLookup.Remove(id); undoableReadReceipts.Remove(id);
        }
    }
    async Task RegisterReadReceiptsAsync(IEnumerable<TaskCompletion> receipts,long version,string visibleScope = null,bool appendVisible = false,long visibleRequest = 0)
    {
        var batch=receipts.Where(r=>r!=null).ToArray();
        bool CurrentRequest() => visibleScope == null || visibleRequest == (visibleScope == "history" ? historyReceiptRequest : calendarReceiptRequest);
#if UNITY_EDITOR
        if (BeforeReceiptRegistrationForVerification != null) await BeforeReceiptRegistrationForVerification(visibleScope);
#endif
        if(version<publishedVersion||!CurrentRequest())return;
        string today=TaskService.DateKey(clock.LocalNow);
        var eligibility=await repository.ReadAsync(tx=>tx.UndoableCompletions(today,batch.Select(r=>r.id)));
        if(eligibility.Succeeded&&eligibility.Version==version&&version>=publishedVersion&&CurrentRequest()) {
            if (visibleScope != null) SetVisibleReceiptScope(visibleScope,batch,appendVisible);
            RegisterReadReceipts(batch,eligibility.Value,visibleScope != null);
        }
    }
    public bool CanUndoCompletion(string id)
    {
        if (string.IsNullOrEmpty(id) || !readReceiptLookup.TryGetValue(id, out var receipt) || receipt.revoked) return false;
        return undoableReadReceipts.Contains(id)&&(receipt.day=="once"||receipt.day==TaskService.DateKey(clock.LocalNow));
    }
    public bool OwnsEquipment(string id) => view.OwnsEquipment(id);
    public string EquippedItem(EquipmentSlot slot) => view.EquippedItem(slot);
    public string ClaimEquipmentError(string id) => view.ClaimEquipmentError(id);
    public string UpgradeEquipmentError(string id, int level) => view.UpgradeEquipmentError(id, level);
    public string BattleEntryError(int id, int stage = 0) => view.BattleEntryError(id, stage);
    public string BossUnlockError(int id, int stage = 0) => view.BossUnlockError(id, stage);
    public string LootError(int count) => view.LootError(count);
    public RewardAmounts PreviewReward(TaskView row) => view.PreviewReward(row);
    public RewardAmounts PreviewDailyGoal(DailyRewardGoal goal) => view.PreviewDailyGoal(goal);
    public RewardAmounts PreviewFocusRewards(double seconds) => view.PreviewFocusRewards(seconds);
    public int PreviewFocusFish(double seconds) => view.PreviewFocusFish(seconds);
    public bool DailyGoalClaimed(DailyRewardGoal goal) => view.DailyGoalClaimed(goal);
    public int DailyGoalProgress(DailyRewardGoal goal) => goal == DailyRewardGoal.Focus25Minutes ? (int)Math.Min(25, Math.Floor(dayTotals.FocusSeconds / 60)) : (int)Math.Min(TaskService.DailyGoalTarget(goal), dayTotals.CompletedTasks);
    public List<TaskView> Query(TaskTab tab, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null) => view.Query(tab, search, difficulty, tags);
    public PlayerData BrowseSnapshot(bool history) => view.BrowseSnapshot(history);
    public sealed class BrowseResult
    {
        public List<QuestTag> tags;
        public List<TaskView> rows;
        public int[] counts;
        public int fishCoins, tickets;
    }
    sealed class BrowseClock : ITaskClock
    {
        public DateTime LocalNow { get; }
        public DateTime UtcNow { get; }
        public BrowseClock(DateTime local, DateTime utc) { LocalNow=local; UtcNow=utc; }
    }
    public Task<OperationResult<BrowseResult>> QueryBrowseAsync(TaskTab tab, string search, TaskDifficulty? difficulty, IEnumerable<string> tags)
    {
        // Published projections are replaced, never mutated. Capture one revision
        // before dispatch; detached rows and filtering are built off the UI thread.
        var source=presentation;long version=publishedVersion;
        var at=new BrowseClock(clock.LocalNow,clock.UtcNow);
        string[] selectedTags=(tags??Array.Empty<string>()).ToArray();
        OperationResult<BrowseResult> Build()
        {
            try {
                var reader=new TaskService(new ReadStore(source),at,false);
                var snapshot=reader.BrowseSnapshot(false);
                selectedTags=selectedTags.Where(id=>snapshot.tags.Any(t=>t.id==id)).ToArray();
                var rows=tab==TaskTab.History?new List<TaskView>():reader.QuerySnapshot(snapshot,tab,search,difficulty,selectedTags);
                if(tab==TaskTab.Todo)rows.AddRange(reader.QuerySnapshot(snapshot,TaskTab.Daily,search,difficulty,selectedTags).Where(row=>!row.Completed));
                return new OperationResult<BrowseResult>{Succeeded=true,Version=version,Value=new BrowseResult{
                    tags=snapshot.tags,rows=rows,counts=Enumerable.Range(0,5).Select(i=>reader.BrowseCount(snapshot,(TaskTab)i)).ToArray(),
                    fishCoins=snapshot.fishCoins,tickets=snapshot.tickets}};
            } catch(Exception error) { return SqlitePlayerRepository.Failure<BrowseResult>(error); }
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        return Task.FromResult(Build());
#else
        return Task.Run(Build);
#endif
    }
    public int BrowseCount(PlayerData data, TaskTab tab) => view.BrowseCount(data, tab);
    public List<TaskView> QuerySnapshot(PlayerData data, TaskTab tab, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null, bool orderResults = true) => view.QuerySnapshot(data, tab, search, difficulty, tags, orderResults);
    public TaskCalendarResult QueryCalendar(DateTime first, DateTime last, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null, bool showCompleted = true) => view.QueryCalendar(first, last, search, difficulty, tags, showCompleted);
    public FocusStatistics.Result QueryFocusStatistics(DateTime date, FocusStatisticsPeriod period, TimeZoneInfo zone) => view.QueryFocusStatistics(date, period, zone);
    public List<QuestTag> FocusStatisticsTagCatalog() => view.FocusStatisticsTagCatalog();
    public BossProgress BossMastery(int id) => view.BossMastery(id);
    public List<TaskView> QueryForChat(string scope, string value) => view.QueryForChat(scope, value);
    public Task<OperationResult<QuestTask>> GetTaskAsync(string id) => repository.ReadAsync(tx => tx.Read<QuestTask>(PlayerRecordKind.Tasks, id));
    public async Task<OperationResult<TaskCompletion>> GetCompletionAsync(string id)
    {
        var result = await repository.ReadAsync(tx => tx.Read<TaskCompletion>(PlayerRecordKind.History, id));
        if (result.Succeeded && result.Version >= publishedVersion) await RegisterReadReceiptsAsync(new[] { result.Value },result.Version);
        return result;
    }
    public Task<OperationResult<FocusSession>> FocusReceiptAsync(string id) => repository.ReadAsync(tx => tx.Read<FocusSession>(PlayerRecordKind.FocusSessions, id));
    public Task<OperationResult<BattleAttempt>> BattleReceiptAsync(string id) => repository.ReadAsync(tx => tx.Read<BattleAttempt>(PlayerRecordKind.Battles, id));
    public string CompletionId(string taskId, string day) => readReceiptLookup.Values.FirstOrDefault(h => h.taskId == taskId && h.day == day && !h.revoked)?.id;
    public int HistoryCount { get; private set; }
    public LootReceipt LastLootReceipt { get; private set; }
    public Task<OperationResult<AccountAggregate>> QueryAccountAggregateAsync(string uid, string name) => repository.QueryAccountAggregateAsync(uid, name);
    public Task<OperationResult<FocusStatistics.Result>> QueryFocusStatisticsAsync(DateTime date, FocusStatisticsPeriod period, TimeZoneInfo zone) => repository.QueryFocusStatisticsAsync(date, period, zone);
    public Task<OperationResult<List<QuestTag>>> QueryFocusStatisticsTagCatalogAsync() => repository.QueryFocusStatisticsTagCatalogAsync();
    public async Task<OperationResult<PlayerHistoryPage>> QueryHistoryPageAsync(string cursor = null, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null, bool registerVisibleReceipts = true)
    {
        long request = registerVisibleReceipts ? Interlocked.Increment(ref historyReceiptRequest) : 0;
        var result = await repository.QueryHistoryPageAsync(cursor, search, difficulty, tags?.ToArray());
        if (result.Succeeded && result.Version >= publishedVersion) { HistoryCount = (int)Math.Min(int.MaxValue, result.Value.TotalCount); if(registerVisibleReceipts) await RegisterReadReceiptsAsync(result.Value.Rows.Select(r => r.history),result.Version,"history",false,request); }
        return result;
    }
    public async Task<OperationResult<TaskCalendarResult>> QueryCalendarAsync(DateTime first, DateTime last, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null, bool showCompleted = true)
    {
        long request = Interlocked.Increment(ref calendarReceiptRequest);
        string[] selected = tags?.ToArray();
        var data = await repository.QueryCalendarDataAsync(first, last, TaskService.DateKey(clock.LocalNow));
        if (!data.Succeeded) return new OperationResult<TaskCalendarResult> { Error = data.Error, Message = data.Message };
        var noDate = await repository.QueryNoDatePageAsync(null, search, difficulty, selected, showCompleted);
        if (!noDate.Succeeded) return new OperationResult<TaskCalendarResult> { Error = noDate.Error, Message = noDate.Message };
        if (noDate.Version != data.Version) return new OperationResult<TaskCalendarResult> { Error = PlayerStorageError.Busy, Message = "Calendar changed. Refresh the calendar." };
        var query = new TaskService(new ReadStore(data.Value), clock, false);
        var result = query.QueryCalendar(first, last, search, difficulty, selected, showCompleted);
        result.noDate = noDate.Value.Rows; result.noDateNextCursor = noDate.Value.NextCursor; result.noDateTotalCount = noDate.Value.TotalCount;
        if (data.Version >= publishedVersion) await RegisterReadReceiptsAsync(result.days.Values.SelectMany(d => d.items).Concat(result.noDate).Select(i => i.view.history),data.Version,"calendar",false,request);
        return new OperationResult<TaskCalendarResult> { Succeeded = true, Version = data.Version, Value = result };
    }
    public async Task<OperationResult<PlayerCalendarPage>> QueryNoDatePageAsync(string cursor = null, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null, bool showCompleted = true, bool appendVisible = false)
    {
        long request = Interlocked.Increment(ref calendarReceiptRequest);
        var result = await repository.QueryNoDatePageAsync(cursor, search, difficulty, tags?.ToArray(), showCompleted);
        if (result.Succeeded && result.Version >= publishedVersion) await RegisterReadReceiptsAsync(result.Value.Rows.Select(r => r.view.history),result.Version,"calendar",appendVisible,request);
        return result;
    }
    public Task<OperationResult<List<TaskView>>> QueryForChatAsync(string scope, string value) => Task.FromResult(new OperationResult<List<TaskView>> { Succeeded = true, Version = publishedVersion, Value = view.QueryForChat(scope, value) });
    public async Task<OperationResult<bool>> TransferBackupAsync(string path, bool export)
    {
        await commands.WaitAsync();
        try {
            if (closed || !IsReady || !acceptingCommands)
                return new OperationResult<bool> { Error = PlayerStorageError.Closed, Message = "Wait for Cat Quest to finish starting or saving." };
            if (export) { var result = await repository.ExportBackupAsync(path); return new OperationResult<bool> { Succeeded = result.Succeeded, Value = result.Succeeded, Error = result.Error, Message = result.Message, Version = result.Version }; }
            var imported = await repository.ImportBackupAsync(path);
            if (imported.Succeeded) {
                bool refreshed = false;
                try { ResetTaskDateCache(); refreshed = (await RefreshPresentationAsync()).Succeeded; }
                catch (Exception) { /* The import is already committed; do not report it as an uncommitted failure. */ }
                if (!refreshed) {
                    return FreezeAfterCommittedProjectionFailure(imported, "Backup imported successfully");
                }
                Publish(RecordsChanged, (IReadOnlyCollection<PlayerRecordKind>)null);
                Publish(Changed);
            }
            LastError = imported.Message; return imported;
        } finally { commands.Release(); }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public Task<OperationResult<PlayerData>> ReadVerificationSnapshotAsync()
    {
        bool isolated = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE"))
            || CompanionProtocol.HasArgument("--focus-smoke") || AccountBuildSmoke.Enabled || AiBuildSmoke.Enabled;
        if (!isolated || !(repository is SqlitePlayerRepository sqlite))
            throw new InvalidOperationException("Full diagnostic snapshots require an explicit isolated verification run.");
        return sqlite.ReadAllForVerificationAsync();
    }
#endif
#if UNITY_EDITOR
    public static SqlitePlayerService CreateReadOnlyPreviewForVerification(PlayerData data, ITaskClock clock)
    {
        var service = new SqlitePlayerService(new SqlitePlayerRepository(":memory:"), clock);
        service.presentation = PlayerDataStore.Copy(data);
        service.view = new TaskService(new ReadStore(service.presentation), clock, false);
        service.IsReady = true; service.acceptingCommands = false;
        return service;
    }
#endif
}
