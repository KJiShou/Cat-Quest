using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using UnityEngine;

// Connection ownership is confined to the FIFO worker. No Unity objects, events,
// or UI callbacks are accessed there. Awaiting callers publish results themselves.
public sealed partial class SqlitePlayerRepository : IPlayerRepository
{
    readonly object queueLock = new object();
    Task tail = Task.CompletedTask;
    SQLiteConnection database;
    FileStream ownership;
    bool closing, initialized;
    readonly string legacyPath;
    public string DatabasePath { get; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public Action<string> FaultPoint;
    public string LastVerificationDiagnostic { get; private set; }
#else
    readonly Action<string> FaultPoint = null;
#endif
    internal long Revision => long.Parse(SqlitePlayerSchema.MetaValue(database, "revision"), System.Globalization.CultureInfo.InvariantCulture);

    public SqlitePlayerRepository(string databasePath, string legacyJsonPath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A storage path is required.");
        DatabasePath = databasePath == ":memory:" ? databasePath : Path.GetFullPath(databasePath);
        legacyPath = legacyJsonPath == null ? null : Path.GetFullPath(legacyJsonPath);
        if (DatabasePath == legacyPath) throw new ArgumentException("The legacy source and database must be different files.");
    }
    Task<OperationResult<T>> Queue<T>(Func<T> operation, bool requireReady = true)
    {
        lock (queueLock) {
            if (closing) return Task.FromResult(Failure<T>(new PlayerRepositoryException(PlayerStorageError.Closed, "Storage is closing.")));
            var previous = tail;
            var queueTimer = System.Diagnostics.Stopwatch.StartNew();
#if UNITY_WEBGL && !UNITY_EDITOR
            var next = Run();
#else
            var next = Task.Run(Run);
#endif
            tail = next;
            return next;
            async Task<OperationResult<T>> Run()
            {
                await previous.ConfigureAwait(false);
                double queueMilliseconds = queueTimer.Elapsed.TotalMilliseconds;
                var workerTimer = System.Diagnostics.Stopwatch.StartNew();
                try {
                    if (requireReady && !initialized) throw new PlayerRepositoryException(PlayerStorageError.Closed, "Storage has not finished starting.");
                    var value = operation();
                    long version = database == null ? 0 : Revision;
                    return new OperationResult<T> { Succeeded = true, Value = value, Version = version, WorkerMilliseconds = workerTimer.Elapsed.TotalMilliseconds, QueueWaitMilliseconds = queueMilliseconds };
                } catch (Exception error) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LastVerificationDiagnostic=error.GetType().Name+"\n"+error.StackTrace;
#endif
                    var failure = Failure<T>(error); failure.WorkerMilliseconds = workerTimer.Elapsed.TotalMilliseconds; failure.QueueWaitMilliseconds = queueMilliseconds; return failure;
                }
            }
        }
    }
    internal static OperationResult<T> Failure<T>(Exception exception)
    {
        var code = PlayerStorageError.Storage;
        string message = "Could not save your changes. Please try again.";
        if (exception is PlayerCommandException command) { code = PlayerStorageError.InvalidData; message = command.Message; }
        else if (exception is PlayerRepositoryException known) { code = known.Code; message = known.Message; }
        else if (exception is OverflowException) { code = PlayerStorageError.InvalidData; message = "The reward balance has reached its maximum."; }
        else if (exception is UnauthorizedAccessException) { code = PlayerStorageError.AccessDenied; message = "The save folder is not writable."; }
        else if (exception is IOException io && ((io.HResult & 0xffff) == 112 || (io.HResult & 0xffff) == 39)) {
            code = PlayerStorageError.DiskFull; message = "Free some disk space, then retry saving.";
        }
        else if (exception is ArgumentException) { code = PlayerStorageError.InvalidData; message = "The save contains invalid or duplicate records."; }
        else if (exception is SQLiteException sqlite) {
            switch (sqlite.Result) {
                case SQLite3.Result.Busy: case SQLite3.Result.Locked: code = PlayerStorageError.Busy; message = "The save is busy. Please try again."; break;
                case SQLite3.Result.Full: code = PlayerStorageError.DiskFull; message = "Free some disk space, then retry saving."; break;
                case SQLite3.Result.ReadOnly: case SQLite3.Result.Perm: code = PlayerStorageError.AccessDenied; message = "The save folder is not writable."; break;
                case SQLite3.Result.Corrupt: case SQLite3.Result.NonDBFile: code = PlayerStorageError.Corrupt; message = "The save needs recovery from a verified backup."; break;
                case SQLite3.Result.Constraint: code = PlayerStorageError.InvalidData; message = "The change conflicts with an existing record."; break;
            }
        }
        return new OperationResult<T> { Error = code, Message = message };
    }
    internal static SQLiteConnection Open(string path, bool readOnly = false)
    {
        var flags = readOnly ? SQLiteOpenFlags.ReadOnly : SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create;
        var db = new SQLiteConnection(path, flags | SQLiteOpenFlags.FullMutex);
        try {
            db.BusyTimeout = TimeSpan.FromSeconds(3);
            db.Execute("PRAGMA foreign_keys=ON");
            db.Execute("PRAGMA trusted_schema=OFF");
            if (!readOnly) db.Execute("PRAGMA synchronous=FULL");
            return db;
        } catch { db.Dispose(); throw; }
    }
    public Task<OperationResult<string>> InitializeAsync() => Queue(() => {
        if (initialized) return database.ExecuteScalar<string>("SELECT sqlite_version()");
        try {
            if (DatabasePath != ":memory:") {
                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
                try { ownership = new FileStream(DatabasePath + ".owner", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { throw new PlayerRepositoryException(PlayerStorageError.Busy, "This save is already open in another Cat Quest instance."); }
            }
            InitializeStorage();
            database ??= Open(DatabasePath);
            UpgradeStorage(database);
            SqlitePlayerSchema.ValidateDatabase(database, false);
            if (DatabasePath != ":memory:") database.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
            initialized = true;
            noDateRevision = -1;
            return database.ExecuteScalar<string>("SELECT sqlite_version()");
        } catch {
            database?.Dispose(); database = null;
            ownership?.Dispose(); ownership = null;
            throw;
        }
    }, false);

    public Task<OperationResult<IReadOnlyList<T>>> QueryAsync<T>(PlayerRecordQuery query) where T : class
    {
        var frozen = Freeze(query);
        return Queue<IReadOnlyList<T>>(() => Query<T>(database, frozen));
    }
    static PlayerRecordQuery Freeze(PlayerRecordQuery q)
    {
        if (q == null || q.Limit < 1 || q.Limit > 100) throw new ArgumentException("Query batches must contain 1–100 records.");
        SqlitePlayerSchema.Get(q.Kind);
        return new PlayerRecordQuery { Kind = q.Kind, Id = q.Id, ParentId = q.ParentId, DayFrom = q.DayFrom,
            DayThrough = q.DayThrough, State = q.State, AfterSequence = q.AfterSequence, Limit = q.Limit };
    }
    static IReadOnlyList<T> Query<T>(SQLiteConnection db, PlayerRecordQuery q) where T : class
    {
        if (SqlitePlayerSchema.Get(q.Kind).Type != typeof(T)) throw new ArgumentException("Query type does not match the collection.");
        var sql = "SELECT payload FROM " + SqlitePlayerSchema.Table(q.Kind) + " WHERE sequence>?";
        var args = new List<object> { q.AfterSequence };
        void Filter(string clause, object value) { sql += " AND " + clause; args.Add(value); }
        if (q.Id != null) Filter("id=?", q.Id);
        if (q.ParentId != null) Filter("parentId=?", q.ParentId);
        if (q.DayFrom != null) Filter("day>=?", q.DayFrom);
        if (q.DayThrough != null) Filter("day<=?", q.DayThrough);
        if (q.State.HasValue) Filter("state=?", q.State.Value);
        sql += " ORDER BY sequence LIMIT ?"; args.Add(q.Limit);
        return db.Query<SqlitePlayerSchema.Row>(sql, args.ToArray()).Select(r => JsonUtility.FromJson<T>(r.payload)).ToArray();
    }
    public async Task<OperationResult<T>> ExecuteAsync<T>(Func<IPlayerTransaction, T> command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        PlayerRecordKind[] changed = Array.Empty<PlayerRecordKind>();
        var result = await Queue(() => {
            var transaction = new Transaction(database);
            string commitId = Guid.NewGuid().ToString("N");
            T value = default;
            bool commandFinished = false;
            long previousRevision = Revision;
            try {
                FaultPoint?.Invoke("before-transaction");
                database.Execute("BEGIN IMMEDIATE");
                FaultPoint?.Invoke("transaction-started");
                value = command(transaction);
                commandFinished = true;
                FaultPoint?.Invoke("before-commit");
                SqlitePlayerSchema.SetMeta(database, "revision", checked(Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                SqlitePlayerSchema.SetMeta(database, "lastCommitId", commitId);
                database.Execute("COMMIT");
                FaultPoint?.Invoke("after-commit");
                changed = transaction.Changed.ToArray();
                AdvanceNoDateCache(changed,previousRevision);
                return value;
            } catch {
                try { database.Execute("ROLLBACK"); } catch (SQLiteException) { /* COMMIT may already have ended the transaction. */ }
                // A commit can succeed even if its acknowledgement fails. The
                // marker belongs to the SAME transaction and the sole writer;
                // never re-execute the domain callback to discover its outcome.
                if (commandFinished && SqlitePlayerSchema.MetaValue(database, "lastCommitId") == commitId) {
                    changed = transaction.Changed.ToArray(); AdvanceNoDateCache(changed,previousRevision); return value;
                }
                throw;
            } finally { transaction.Active = false; }
        });
        result.Changed = changed;
        return result;
    }
    public Task<OperationResult<T>> ReadAsync<T>(Func<IPlayerTransaction, T> query) => Queue(() => {
        var transaction = new Transaction(database, true);
        try { FaultPoint?.Invoke("read-started"); database.Execute("BEGIN"); var value = query(transaction); database.Execute("COMMIT"); return value; }
        catch { try { database.Execute("ROLLBACK"); } catch (SQLiteException) { } throw; }
        finally { transaction.Active = false; }
    });
    sealed class Transaction : IPlayerTransaction
    {
        readonly SQLiteConnection db;
        readonly bool readOnly;
        internal readonly HashSet<PlayerRecordKind> Changed = new HashSet<PlayerRecordKind>();
        internal bool Active = true;
        internal Transaction(SQLiteConnection db, bool readOnly = false) { this.db = db; this.readOnly = readOnly; }
        void Check() { if (!Active) throw new InvalidOperationException("The transaction has ended."); }
        public T Read<T>(PlayerRecordKind kind, string id) where T : class { Check(); return string.IsNullOrEmpty(id) ? null : Query<T>(new PlayerRecordQuery { Kind = kind, Id = id, Limit = 1 }).FirstOrDefault(); }
        public IReadOnlyList<T> Query<T>(PlayerRecordQuery query) where T : class { Check(); return SqlitePlayerRepository.Query<T>(db, Freeze(query)); }
        public void Put<T>(PlayerRecordKind kind, T record) where T : class { Check(); if (readOnly) throw new InvalidOperationException("Read-only query."); SqlitePlayerSchema.Put(db, SqlitePlayerSchema.Get(kind), record); Changed.Add(kind); if(record is TaskOccurrence occurrence&&occurrence.day=="once")Changed.Add(PlayerRecordKind.Tasks); }
        public void Delete(PlayerRecordKind kind, string id) {
            Check();
            if (readOnly || string.IsNullOrEmpty(id)) throw new InvalidOperationException("Invalid record deletion.");
            if (SqlitePlayerSchema.Get(kind).Singleton) throw new ArgumentException("Singleton records cannot be deleted by a command.");
            if (kind == PlayerRecordKind.History)
                db.Execute("UPDATE cq_totals SET taskCount=taskCount-(SELECT COUNT(*) FROM cq_History WHERE id=? AND state=0) WHERE id=1", id);
            if (kind == PlayerRecordKind.FocusSessions)
                db.Execute("UPDATE cq_totals SET focusSeconds=focusSeconds-COALESCE((SELECT eligibleSeconds FROM cq_FocusSessions WHERE id=? AND state IN (?,?)),0) WHERE id=1",
                    id, (int)FocusSessionStatus.Completed, (int)FocusSessionStatus.EndedEarly);
            if(kind==PlayerRecordKind.Occurrences) {
                db.Execute("UPDATE cq_Tasks SET state=0 WHERE state=2 AND id IN (SELECT parentId FROM cq_Occurrences WHERE id=? AND day='once')",id);
                Changed.Add(PlayerRecordKind.Tasks);
            }
            db.Execute("DELETE FROM " + SqlitePlayerSchema.Table(kind) + " WHERE id=?", id); Changed.Add(kind);
        }
        public TaskCompletion FindCompletion(string taskId, string occurrenceDay) {
            Check(); var json = db.ExecuteScalar<string>("SELECT payload FROM cq_History WHERE parentId=? AND occurrenceDay=?", taskId, occurrenceDay);
            return json == null ? null : JsonUtility.FromJson<TaskCompletion>(json);
        }
        public PlayerDayTotals ReadDayTotals(string day) {
            Check();
            return new PlayerDayTotals {
                TaskExperience = db.ExecuteScalar<long>("SELECT COALESCE(SUM(experience),0) FROM cq_History WHERE rewardDay=?", day),
                TaskFishCoins = db.ExecuteScalar<long>("SELECT COALESCE(SUM(fishCoins),0) FROM cq_History WHERE rewardDay=?", day),
                TaskTickets = db.ExecuteScalar<long>("SELECT COALESCE(SUM(tickets),0) FROM cq_History WHERE rewardDay=?", day),
                RewardReceiptCount = db.ExecuteScalar<long>("SELECT COUNT(*) FROM cq_History WHERE rewardDay=?", day),
                CompletedTasks = db.ExecuteScalar<long>("SELECT COUNT(*) FROM cq_History WHERE day=? AND state=0", day),
                FocusSeconds = db.ExecuteScalar<double>("SELECT COALESCE(SUM(eligibleSeconds),0) FROM cq_FocusSessions WHERE rewardDay=? AND state IN (?,?)", day, (int)FocusSessionStatus.Completed, (int)FocusSessionStatus.EndedEarly)
            };
        }
        public IReadOnlyList<QuestTask> ActiveTasks(string tagId = null, bool includeCompleted = false) {
            Check(); string sql = "SELECT payload FROM cq_Tasks WHERE " + (includeCompleted ? "1=1" : "state IN (0,1)"); object[] args = Array.Empty<object>();
            if (tagId != null) { sql += " AND id IN (SELECT taskId FROM cq_task_tags WHERE tagId=?)"; args = new object[] { tagId }; }
            return db.Query<SqlitePlayerSchema.Row>(sql + " ORDER BY sequence", args).Select(r => JsonUtility.FromJson<QuestTask>(r.payload)).ToArray();
        }
        public IReadOnlyList<TaskOccurrence> CurrentOccurrences(string today) {
            Check();
            return db.Query<SqlitePlayerSchema.Row>("SELECT o.payload AS payload,o.sequence AS sequence FROM cq_Tasks t INDEXED BY cq_Tasks_state CROSS JOIN cq_Occurrences o ON t.id=o.parentId AND o.day=? WHERE t.state=1 UNION ALL SELECT o.payload AS payload,o.sequence AS sequence FROM cq_Tasks t INDEXED BY cq_Tasks_state CROSS JOIN cq_Occurrences o ON t.id=o.parentId AND o.day='once' WHERE t.state=0 ORDER BY 2", today)
                .Select(r => JsonUtility.FromJson<TaskOccurrence>(r.payload)).ToArray();
        }
        public IReadOnlyList<TaskCompletion> CurrentDailyCompletions(string today) {
            Check();
            return db.Query<SqlitePlayerSchema.Row>("SELECT h.payload FROM cq_Tasks t JOIN cq_History h ON t.id=h.parentId AND h.occurrenceDay=? WHERE t.state=1 ORDER BY h.sequence", today)
                .Select(r=>JsonUtility.FromJson<TaskCompletion>(r.payload)).ToArray();
        }
        public IReadOnlyList<string> UndoableCompletions(string today,IEnumerable<string> receiptIds) {
            Check();var ids=receiptIds.Distinct().ToArray();var result=new List<string>();
            for(int offset=0;offset<ids.Length;offset+=100) {
                var batch=ids.Skip(offset).Take(100).ToArray();var args=new List<object>{today};args.AddRange(batch.Cast<object>());
                result.AddRange(db.Query<SqlitePlayerSchema.Row>("SELECT h.id FROM cq_History h JOIN cq_Tasks t ON t.id=h.parentId JOIN cq_Occurrences o ON o.parentId=t.id AND o.day=h.occurrenceDay WHERE h.state=0 AND o.state=1 AND h.occurrenceDay=CASE WHEN t.state=1 THEN ? ELSE 'once' END AND h.id IN ("+string.Join(",",batch.Select(_=>"?"))+")",args.ToArray()).Select(r=>r.id));
            }
            return result;
        }
        public IReadOnlyList<EquipmentItem> Equipment() { Check();return db.Query<SqlitePlayerSchema.Row>("SELECT payload FROM cq_Equipment ORDER BY sequence").Select(r=>JsonUtility.FromJson<EquipmentItem>(r.payload)).ToArray(); }
        public int ReadMailState(string id)
        {
            Check();
            return db.ExecuteScalar<int>("SELECT state FROM cq_mail_state WHERE id=?", id);
        }

        public IReadOnlyList<string> ReadMailKeys(string prefix)
        {
            Check();
            return db.Query<SavedMailState>("SELECT id FROM cq_mail_state WHERE id GLOB ?", prefix + "*")
                .Select(row => row.id.Substring(prefix.Length)).ToArray();
        }

        public void SetMailState(string id, int state)
        {
            Check();
            if (readOnly) throw new InvalidOperationException("Read-only query.");
            if (db.Execute("INSERT INTO cq_mail_state VALUES(?,?) ON CONFLICT(id) DO UPDATE SET state=excluded.state WHERE cq_mail_state.state<excluded.state", id, state) > 0)
                Changed.Add(PlayerRecordKind.Mail);
        }

        public IReadOnlyList<QuestTag> Tags() { Check(); return db.Query<SqlitePlayerSchema.Row>("SELECT payload FROM cq_Tags ORDER BY sequence").Select(r => JsonUtility.FromJson<QuestTag>(r.payload)).ToArray(); }
    }
    public Task<OperationResult<bool>> FlushAsync() => Queue(() => true);
    public Task CloseAsync()
    {
        lock (queueLock) {
            if (closing) return tail;
            closing = true;
            maintenanceCancellation?.Cancel();
            var maintenance = maintenanceTask;
            var previous = tail;
            async Task Finish() {
                await previous.ConfigureAwait(false);
                if (maintenance != null) await maintenance.ConfigureAwait(false);
                try { database?.Dispose(); }
                finally { database = null; initialized = false; ownership?.Dispose(); ownership = null; }
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            tail = Finish();
#else
            tail = Task.Run(Finish);
#endif
            return tail;
        }
    }
    public async Task<OperationResult<string>> ReopenAsync()
    {
        Task previous;
        lock (queueLock) previous = tail;
        await previous.ConfigureAwait(false);
        lock (queueLock) { if (closing) closing = false; }
        return await InitializeAsync().ConfigureAwait(false);
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public Task<OperationResult<PlayerData>> ReadAllForVerificationAsync() => Queue(() => SqlitePlayerSchema.ReadAll(database));
    public long VerificationSqlCount { get; private set; }
    sealed class VerificationQueryPlan { public string detail { get; set; } }
    public Task<OperationResult<string[]>> ExplainForVerificationAsync() => Queue(() => {
        var lines=new List<string> {
            "writer synchronous="+database.ExecuteScalar<int>("PRAGMA synchronous"),
            "writer journal_mode="+database.ExecuteScalar<string>("PRAGMA journal_mode"),
            "writer foreign_keys="+database.ExecuteScalar<int>("PRAGMA foreign_keys"),
            "writer busy_timeout="+database.ExecuteScalar<int>("PRAGMA busy_timeout")
        };
        void Explain(string label,string sql,params object[] args) {
            lines.Add(label+": "+sql);
            lines.AddRange(database.Query<VerificationQueryPlan>("EXPLAIN QUERY PLAN "+sql,args).Select(row=>"  "+row.detail));
        }
        Explain("Active tasks","SELECT payload FROM cq_Tasks WHERE state IN (0,1) ORDER BY sequence");
        Explain("Current occurrence","SELECT o.payload FROM cq_Tasks t INDEXED BY cq_Tasks_state CROSS JOIN cq_Occurrences o ON t.id=o.parentId AND o.day=? WHERE t.state=0 ORDER BY o.sequence","once");
        Explain("Task identity","SELECT payload FROM cq_Tasks WHERE sequence>? AND id=? ORDER BY sequence LIMIT ?",-1,"active-0",1);
        Explain("Completion identity","SELECT payload FROM cq_History WHERE parentId=? AND occurrenceDay=?","active-0","once");
        Explain("Daily receipt totals","SELECT COALESCE(SUM(experience),0) FROM cq_History WHERE rewardDay=?","2026-11-16");
        Explain("History metadata page","SELECT h.* FROM cq_history_search h WHERE h.state=0 ORDER BY h.sortKey DESC,h.sequence LIMIT 101");
        Explain("History difficulty and snapshot tag","SELECT h.* FROM cq_history_search h WHERE h.state=0 AND h.difficulty=? AND h.id IN (SELECT historyId FROM cq_history_tags WHERE tagId=?) ORDER BY h.sortKey DESC,h.sequence LIMIT 101",(int)TaskDifficulty.Normal,"tag");
        Explain("Calendar month","SELECT payload FROM cq_History WHERE state=0 AND calendarDay>=? AND calendarDay<=? ORDER BY sequence","2026-11-01","2026-11-30");
        Explain("No Date metadata fallback (intentional scan, cached until relevant data changes)","SELECT * FROM cq_calendar_nodate ORDER BY sequence");
        return lines.ToArray();
    });
    public Task<OperationResult<bool>> CountSqlForVerificationAsync() => Queue(() => {
        VerificationSqlCount = 0;
        database.Tracer = _ => VerificationSqlCount++;
        database.Trace = true;
        return true;
    });
#endif
}
