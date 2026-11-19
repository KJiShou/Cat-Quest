using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SQLite;
using UnityEngine;

// Payloads contain one entity, never a collection or complete PlayerData. Indexed
// columns are derived from that entity by this class, never supplied by a caller.
internal static class SqlitePlayerSchema
{
    internal const int Version = 3, ApplicationId = 0x43515154;
    internal sealed class Row
    {
        public string id { get; set; }
        public string parentId { get; set; }
        public string day { get; set; }
        public int state { get; set; }
        public long sequence { get; set; }
        public string payload { get; set; }
        public byte[] sortKey { get; set; }
        public double eligibleSeconds { get; set; }
        public string rewardDay { get; set; }
        public string occurrenceDay { get; set; }
        public string calendarDay { get; set; }
        public long experience { get; set; }
        public long fishCoins { get; set; }
        public long tickets { get; set; }
        public long? beginTicks { get; set; }
        public long? endTicks { get; set; }
    }
    internal sealed class Meta { public string value { get; set; } }
    [Serializable] internal sealed class CalendarTagIds { public List<string> ids; }
    internal sealed class Mapping
    {
        public PlayerRecordKind Kind;
        public string Field;
        public Type Type;
        public Func<object, string> Id, Parent, Day;
        public Func<object, int> State;
        public bool Singleton => Field == null || !typeof(IList).IsAssignableFrom(typeof(PlayerData).GetField(Field).FieldType);
    }
    internal static readonly Mapping[] Mappings = {
        Map<PlayerData>(PlayerRecordKind.Profile, null, _ => "profile"),
        Map<AppSettings>(PlayerRecordKind.Settings, "settings", _ => "settings"),
        Map<AccountPreferences>(PlayerRecordKind.Account, "account", _ => "account"),
        Map<TutorialProgress>(PlayerRecordKind.Tutorial, "tutorial", _ => "tutorial"),
        Map<QuestTask>(PlayerRecordKind.Tasks, "tasks", x => x.id, day: x => x.date, state: x => x.daily ? 1 : 0),
        Map<QuestTag>(PlayerRecordKind.Tags, "tags", x => x.id),
        Map<TaskOccurrence>(PlayerRecordKind.Occurrences, "occurrences", x => OccurrenceId(x.taskId, x.day), x => x.taskId, x => x.day, x => x.completed ? 1 : 0),
        Map<TaskCompletion>(PlayerRecordKind.History, "history", x => x.id, x => x.taskId, x => x.completedLocalDay, x => x.revoked ? 1 : 0),
        Map<RewardDay>(PlayerRecordKind.RewardDays, "rewardDays", x => x.day, day: x => x.day),
        Map<FocusSession>(PlayerRecordKind.FocusSessions, "focusSessions", x => x.id, x => x.questId, x => x.rewardDay, x => (int)x.status),
        Map<EquipmentItem>(PlayerRecordKind.Equipment, "equipment", x => x.id),
        Map<EquipmentUpgradeReceipt>(PlayerRecordKind.EquipmentUpgrades, "equipmentUpgrades", x => x.id, x => x.itemId),
        Map<LootReceipt>(PlayerRecordKind.LootReceipts, "lootReceipts", x => x.id),
        Map<BattleAttempt>(PlayerRecordKind.Battles, "battles", x => x.id, state: x => (int)x.outcome),
        Map<BossProgress>(PlayerRecordKind.BossProgress, "bossProgress", x => x.bossId.ToString(System.Globalization.CultureInfo.InvariantCulture))
    };
    static Mapping Map<T>(PlayerRecordKind kind, string field, Func<T, string> id,
        Func<T, string> parent = null, Func<T, string> day = null, Func<T, int> state = null)
        => new Mapping { Kind = kind, Field = field, Type = typeof(T), Id = o => id((T)o),
            Parent = o => parent?.Invoke((T)o), Day = o => day?.Invoke((T)o), State = o => state?.Invoke((T)o) ?? 0 };
    internal static string OccurrenceId(string task, string day) => task?.Length + ":" + task + day;
    internal static Mapping Get(PlayerRecordKind kind) => Mappings.Single(m => m.Kind == kind);
    internal static string Table(PlayerRecordKind kind) => "cq_" + Get(kind).Kind;
    internal static void Create(SQLiteConnection db)
    {
        db.Execute("PRAGMA application_id=" + ApplicationId);
        db.Execute("PRAGMA user_version=" + Version);
        db.Execute("CREATE TABLE cq_meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
        foreach (var m in Mappings) {
            string table = Table(m.Kind);
            db.Execute("CREATE TABLE " + table + " (id TEXT PRIMARY KEY NOT NULL, parentId TEXT, day TEXT, state INTEGER NOT NULL, sequence INTEGER NOT NULL UNIQUE, payload TEXT NOT NULL, rewardDay TEXT, occurrenceDay TEXT, experience INTEGER NOT NULL DEFAULT 0, fishCoins INTEGER NOT NULL DEFAULT 0, tickets INTEGER NOT NULL DEFAULT 0, eligibleSeconds REAL NOT NULL DEFAULT 0, calendarDay TEXT, sortKey BLOB, beginTicks INTEGER, endTicks INTEGER)");
            db.Execute("CREATE INDEX " + table + "_parent ON " + table + "(parentId,day)");
            db.Execute("CREATE INDEX " + table + "_day ON " + table + "(day,state,sequence)");
            db.Execute("CREATE INDEX " + table + "_state ON " + table + "(state,sequence)");
        }
        db.Execute("CREATE UNIQUE INDEX cq_occurrence_identity ON cq_Occurrences(parentId,day)");
        db.Execute("CREATE UNIQUE INDEX cq_completion_identity ON cq_History(parentId,occurrenceDay)");
        db.Execute("CREATE INDEX cq_reward_day ON cq_History(rewardDay)");
        db.Execute("CREATE INDEX cq_focus_reward_day ON cq_FocusSessions(rewardDay,state)");
        db.Execute("CREATE INDEX cq_history_order ON cq_History(state,sortKey DESC,sequence)");
        db.Execute("CREATE INDEX cq_calendar_history ON cq_History(state,calendarDay)");
        db.Execute("CREATE TABLE cq_calendar_nodate (id TEXT PRIMARY KEY REFERENCES cq_History(id) ON DELETE CASCADE, title TEXT, searchText TEXT, difficulty INTEGER NOT NULL, tags TEXT NOT NULL, sequence INTEGER NOT NULL)");
        db.Execute("CREATE INDEX cq_focus_interval ON cq_FocusSessions(endTicks,beginTicks)");
        db.Execute("CREATE TABLE cq_task_tags (taskId TEXT NOT NULL REFERENCES cq_Tasks(id) ON DELETE CASCADE, tagId TEXT NOT NULL, ordinal INTEGER NOT NULL, PRIMARY KEY(taskId,tagId))");
        db.Execute("CREATE INDEX cq_task_tags_tag ON cq_task_tags(tagId,taskId)");
        db.Execute("CREATE TABLE cq_subtasks (taskId TEXT NOT NULL REFERENCES cq_Tasks(id) ON DELETE CASCADE, id TEXT NOT NULL, ordinal INTEGER NOT NULL, title TEXT, PRIMARY KEY(taskId,id))");
        db.Execute("CREATE TABLE cq_focus_tags (sessionId TEXT NOT NULL REFERENCES cq_FocusSessions(id) ON DELETE CASCADE, tagId TEXT NOT NULL, PRIMARY KEY(sessionId,tagId))");
        db.Execute("CREATE INDEX cq_focus_tag_ids ON cq_focus_tags(tagId)");
        db.Execute("CREATE TABLE cq_totals (id INTEGER PRIMARY KEY CHECK(id=1), taskCount INTEGER NOT NULL, focusSeconds REAL NOT NULL)");
        db.Execute("INSERT INTO cq_totals VALUES(1,0,0)");
        CreateHistoryIndex(db);
        CreateMailTable(db);
        SetMeta(db, "revision", "0");
        SetMeta(db, "format", "CatQuest");
        SetMeta(db, "sortCulture", System.Globalization.CultureInfo.CurrentCulture.Name);
    }
    internal static void ValidateDatabase(SQLiteConnection db, bool full, bool allowPrevious = false)
    {
        if (db.ExecuteScalar<int>("PRAGMA application_id") != ApplicationId)
            throw new PlayerRepositoryException(PlayerStorageError.InvalidData, "Choose a Cat Quest database.");
        int version = db.ExecuteScalar<int>("PRAGMA user_version");
        if (version > Version) throw new PlayerRepositoryException(PlayerStorageError.NewerVersion, "Open this save with a newer version of Cat Quest.");
        if (version != Version && !(allowPrevious && (version == 1 || version == 2))) throw new PlayerRepositoryException(PlayerStorageError.InvalidData, "The database schema is unsupported.");
        if (db.ExecuteScalar<string>(full ? "PRAGMA integrity_check" : "PRAGMA quick_check") != "ok" || db.ExecuteScalar<string>("PRAGMA foreign_key_check") != null)
            throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "The database needs recovery from a verified backup.");
        foreach (var m in Mappings) {
            var columns = new HashSet<string>(db.GetTableInfo(Table(m.Kind)).Select(c => c.Name));
            if (!new[] { "id", "parentId", "day", "state", "sequence", "payload", "rewardDay", "occurrenceDay", "experience", "fishCoins", "tickets", "eligibleSeconds", "calendarDay", "sortKey", "beginTicks", "endTicks" }.All(columns.Contains))
                throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "The database schema is incomplete. Restore a verified backup.");
            if (m.Singleton) {
                string table = Table(m.Kind), id = m.Id(null);
                int count = db.ExecuteScalar<int>("SELECT count(*) FROM " + table);
                // A pre-tutorial save intentionally has null tutorial state.
                // Keep that absence until the tutorial initialization command;
                // migration must not fabricate a row and change the source digest.
                if (m.Kind == PlayerRecordKind.Tutorial && count == 0) continue;
                if (count != 1 || db.ExecuteScalar<int>("SELECT count(*) FROM " + table + " WHERE id=?", id) != 1)
                    throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "Required save settings are missing. Restore a verified backup.");
                if (full && JsonUtility.FromJson(db.ExecuteScalar<string>("SELECT payload FROM " + table + " WHERE id=?", id), m.Type) == null)
                    throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "Required save settings are invalid. Restore a verified backup.");
            }
        }
        foreach (string table in new[] { "cq_meta", "cq_task_tags", "cq_subtasks", "cq_focus_tags", "cq_totals", "cq_calendar_nodate" })
            if (db.GetTableInfo(table).Count == 0) throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "The database schema is incomplete. Restore a verified backup.");
        if (MetaValue(db, "format") != "CatQuest") throw new PlayerRepositoryException(PlayerStorageError.InvalidData, "Invalid save identity.");
        if (version >= 2 && (db.GetTableInfo("cq_history_search").Count == 0 || db.GetTableInfo("cq_history_tags").Count == 0))
            throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "History indexes are missing.");
        if (version >= 3 && db.GetTableInfo("cq_mail_state").Count != 2)
            throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "Mail records are missing. Restore a verified backup.");
        if (!full) return;
        ValidateHistoryIndex(db, version >= 2);
        long taskCount = db.ExecuteScalar<long>("SELECT count(*) FROM cq_History WHERE state=0");
        double seconds = db.ExecuteScalar<double>("SELECT COALESCE(SUM(eligibleSeconds),0) FROM cq_FocusSessions WHERE state=? OR state=?", (int)FocusSessionStatus.Completed, (int)FocusSessionStatus.EndedEarly);
        double storedSeconds = db.ExecuteScalar<double>("SELECT focusSeconds FROM cq_totals WHERE id=1");
        if (db.ExecuteScalar<int>("SELECT count(*) FROM cq_totals") != 1 || db.ExecuteScalar<long>("SELECT taskCount FROM cq_totals WHERE id=1") != taskCount
            || double.IsNaN(storedSeconds) || double.IsInfinity(storedSeconds) || Math.Abs(seconds - storedSeconds) > 1e-6)
            throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "The saved summaries do not match the records. Restore a verified backup.");
        ValidateFocusIndex(db);
    }
    static bool SameText(string a, string b) => (a ?? "") == (b ?? "");
    static void FocusInterval(FocusSession session, out long? beginTicks, out long? endTicks)
    {
        beginTicks = endTicks = null;
        double seconds = session.elapsedSeconds > 0 ? session.elapsedSeconds : session.eligibleSeconds > 0 ? session.eligibleSeconds : session.eligibleMinutes * 60d;
        if (DateTime.TryParse(session.completedUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var end)
            && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= 0 && seconds <= (end - DateTime.MinValue).TotalSeconds) {
            beginTicks = end.AddSeconds(-seconds).Ticks; endTicks = end.Ticks;
        }
    }
    static void ValidateFocusIndex(SQLiteConnection db)
    {
        long after = long.MinValue;
        while (true) {
            var batch = db.Query<Row>("SELECT * FROM cq_FocusSessions WHERE sequence>? ORDER BY sequence LIMIT 100", after);
            if (batch.Count == 0) break;
            var ids = batch.Select(r=>(object)r.id).ToArray();
            var tags = db.Query<Row>("SELECT sessionId AS parentId,tagId AS id FROM cq_focus_tags WHERE sessionId IN ("+string.Join(",",ids.Select(_=>"?"))+")",ids).ToLookup(r=>r.parentId,r=>r.id,StringComparer.Ordinal);
            foreach (var row in batch) {
                var session = JsonUtility.FromJson<FocusSession>(row.payload);
                if(session==null)throw new PlayerRepositoryException(PlayerStorageError.Corrupt,"A Focus record is invalid.");
                FocusInterval(session,out var begin,out var end);
                double eligible = session.eligibleSeconds > 0 ? session.eligibleSeconds : session.eligibleMinutes * 60d;
                var expected = new HashSet<string>((session.tagIds ?? new List<string>()).Where(t=>!string.IsNullOrEmpty(t)),StringComparer.Ordinal);
                if(row.id!=session.id||!SameText(row.parentId,session.questId)||!SameText(row.day,session.rewardDay)||!SameText(row.rewardDay,session.rewardDay)
                    ||row.state!=(int)session.status||row.eligibleSeconds!=eligible||row.beginTicks!=begin||row.endTicks!=end||!expected.SetEquals(tags[row.id]))
                    throw new PlayerRepositoryException(PlayerStorageError.Corrupt,"The saved Focus indexes do not match the records. Restore a verified backup.");
            }
            after=batch[batch.Count-1].sequence;
        }
    }
    internal sealed class HistoryIndexRow
    {
        public string id { get; set; }
        public string searchText { get; set; }
        public int difficulty { get; set; }
        public int state { get; set; }
        public long sequence { get; set; }
        public byte[] sortKey { get; set; }
    }
    static string HistorySearch(TaskCompletion receipt) => (receipt.snapshot.title ?? "") + " " + receipt.snapshot.description + " " + string.Join(" ", receipt.tagNames);
    static void CreateHistoryIndex(SQLiteConnection db)
    {
        db.Execute("CREATE TABLE cq_history_search(id TEXT PRIMARY KEY NOT NULL REFERENCES cq_History(id) ON DELETE CASCADE, searchText TEXT NOT NULL, difficulty INTEGER NOT NULL, state INTEGER NOT NULL, sequence INTEGER NOT NULL, sortKey BLOB NOT NULL)");
        db.Execute("CREATE INDEX cq_history_search_order ON cq_history_search(state,sortKey DESC,sequence)");
        db.Execute("CREATE INDEX cq_history_search_difficulty ON cq_history_search(state,difficulty,sortKey DESC,sequence)");
        db.Execute("CREATE TABLE cq_history_tags(historyId TEXT NOT NULL REFERENCES cq_History(id) ON DELETE CASCADE, tagId TEXT NOT NULL, PRIMARY KEY(historyId,tagId))");
        db.Execute("CREATE INDEX cq_history_tags_tag ON cq_history_tags(tagId,historyId)");
    }
    static void PutHistoryIndex(SQLiteConnection db, TaskCompletion receipt, long sequence, byte[] sortKey)
    {
        db.Execute("INSERT INTO cq_history_search VALUES(?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET searchText=excluded.searchText,difficulty=excluded.difficulty,state=excluded.state,sequence=excluded.sequence,sortKey=excluded.sortKey",
            receipt.id, HistorySearch(receipt), (int)receipt.snapshot.difficulty, receipt.revoked ? 1 : 0, sequence, sortKey);
        db.Execute("DELETE FROM cq_history_tags WHERE historyId=?", receipt.id);
        foreach (string tag in receipt.snapshot.tagIds.Distinct(StringComparer.Ordinal)) db.Execute("INSERT INTO cq_history_tags VALUES(?,?)", receipt.id, tag);
    }
    static void CreateMailTable(SQLiteConnection db)
        => db.Execute("CREATE TABLE cq_mail_state (id TEXT PRIMARY KEY NOT NULL, state INTEGER NOT NULL CHECK(state IN (1,2)))");

    internal static void UpgradePrevious(SQLiteConnection db, Action fault)
    {
        ValidateDatabase(db, true, true);
        int version = db.ExecuteScalar<int>("PRAGMA user_version");
        if (version == Version) return;
        db.RunInTransaction(() => {
            if (version == 1) {
                CreateHistoryIndex(db);
                foreach (var row in db.Query<Row>("SELECT id,payload,sequence,sortKey FROM cq_History ORDER BY sequence"))
                    PutHistoryIndex(db, JsonUtility.FromJson<TaskCompletion>(row.payload), row.sequence, row.sortKey);
            }
            CreateMailTable(db);
            fault?.Invoke();
            db.Execute("PRAGMA user_version=" + Version);
            ValidateDatabase(db, true);
        });
    }
    static void ValidateHistoryIndex(SQLiteConnection db, bool validateSearch)
    {
        if (validateSearch && db.ExecuteScalar<long>("SELECT COUNT(*) FROM cq_History") != db.ExecuteScalar<long>("SELECT COUNT(*) FROM cq_history_search"))
            throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "History search index is incomplete.");
        var sortComparer=System.Globalization.CultureInfo.GetCultureInfo(MetaValue(db,"sortCulture")).CompareInfo;
        long after = long.MinValue;
        while (true) {
            var batch = db.Query<Row>("SELECT * FROM cq_History WHERE sequence>? ORDER BY sequence LIMIT 100", after);
            if (batch.Count == 0) break;
            var ids = batch.Select(r=>(object)r.id).ToArray();
            string placeholders = string.Join(",", ids.Select(_=>"?"));
            var indexes = !validateSearch ? null : db.Query<HistoryIndexRow>("SELECT * FROM cq_history_search WHERE id IN ("+placeholders+")", ids).ToDictionary(r=>r.id,StringComparer.Ordinal);
            var tags = !validateSearch ? null : db.Query<Row>("SELECT historyId AS parentId,tagId AS id FROM cq_history_tags WHERE historyId IN ("+placeholders+")",ids).ToLookup(r=>r.parentId,r=>r.id,StringComparer.Ordinal);
            foreach (var row in batch) {
                var receipt = JsonUtility.FromJson<TaskCompletion>(row.payload);
                if(receipt==null||receipt.snapshot==null)throw new PlayerRepositoryException(PlayerStorageError.Corrupt,"A completion record is invalid.");
                string calendarDay=receipt.snapshot.daily?receipt.day:receipt.snapshot.scheduled?receipt.snapshot.date:null;
                var expectedSort=receipt.completedUtc==null?Array.Empty<byte>():sortComparer.GetSortKey(receipt.completedUtc).KeyData;
                bool sameSort=row.sortKey!=null&&(row.sortKey.SequenceEqual(expectedSort)||(string.IsNullOrEmpty(receipt.completedUtc)&&row.sortKey.Length==0));
                if(row.id!=receipt.id||!SameText(row.parentId,receipt.taskId)||!SameText(row.day,receipt.completedLocalDay)||row.state!=(receipt.revoked?1:0)
                    ||!SameText(row.rewardDay,receipt.rewardedLocalDay)||!SameText(row.occurrenceDay,receipt.day)||!SameText(row.calendarDay,calendarDay)
                    ||row.experience!=receipt.experience||row.fishCoins!=receipt.fishCoins||row.tickets!=receipt.tickets||!sameSort)
                    throw new PlayerRepositoryException(PlayerStorageError.Corrupt,"The saved completion indexes do not match the records. Restore a verified backup.");
                if (!validateSearch) continue;
                if (!indexes.TryGetValue(row.id,out var indexed) || indexed.searchText != HistorySearch(receipt) || indexed.difficulty != (int)receipt.snapshot.difficulty
                    || indexed.state != (receipt.revoked ? 1 : 0) || indexed.sequence != row.sequence || !indexed.sortKey.SequenceEqual(row.sortKey)
                    || !new HashSet<string>(receipt.snapshot.tagIds, StringComparer.Ordinal).SetEquals(tags[row.id]))
                    throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "History search index does not match saved records.");
            }
            after = batch[batch.Count-1].sequence;
        }
    }

    internal static string MetaValue(SQLiteConnection db, string key) => db.Query<Meta>("SELECT value FROM cq_meta WHERE key=?", key).FirstOrDefault()?.value;
    internal static void SetMeta(SQLiteConnection db, string key, string value)
        => db.Execute("INSERT INTO cq_meta(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value);
    internal static PlayerData Profile(PlayerData source)
    {
        var target = new PlayerData();
        foreach (var field in typeof(PlayerData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            field.SetValue(target, field.FieldType.IsValueType || field.FieldType == typeof(string) ? field.GetValue(source) : null);
        return target;
    }
    internal static Row Encode(Mapping map, object value, long sequence)
    {
        if (value == null || value.GetType() != map.Type) throw new ArgumentException("The record does not match its collection.");
        string id = map.Id(value);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A record is missing its identity.");
        if (value is TaskOccurrence o && (string.IsNullOrWhiteSpace(o.taskId) || string.IsNullOrWhiteSpace(o.day))) throw new ArgumentException("Invalid occurrence identity.");
        return new Row { id = id, parentId = map.Parent(value), day = map.Day(value), state = map.State(value), sequence = sequence,
            payload = JsonUtility.ToJson(map.Kind == PlayerRecordKind.Profile ? Profile((PlayerData)value) : value) };
    }
    internal static void Put(SQLiteConnection db, Mapping map, object value, long? order = null)
    {
        var row = Encode(map, value, 0);
        string table = Table(map.Kind);
        var existing = db.Query<Row>("SELECT * FROM " + table + " WHERE id=?", row.id).FirstOrDefault();
        row.sequence = order ?? existing?.sequence ?? db.ExecuteScalar<long>("SELECT COALESCE(MAX(sequence),-1)+1 FROM " + table);
        db.Execute("INSERT INTO " + table + "(id,parentId,day,state,sequence,payload) VALUES(?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET parentId=excluded.parentId,day=excluded.day,state=excluded.state,payload=excluded.payload",
            row.id, row.parentId, row.day, row.state, row.sequence, row.payload);
        if (value is TaskCompletion receipt) {
            db.Execute("UPDATE cq_totals SET taskCount=taskCount+? WHERE id=1", (receipt.revoked ? 0 : 1) - (existing != null && existing.state == 0 ? 1 : 0));
            var sortKey = receipt.completedUtc == null ? Array.Empty<byte>() : System.Globalization.CultureInfo.GetCultureInfo(MetaValue(db, "sortCulture")).CompareInfo.GetSortKey(receipt.completedUtc).KeyData;
            string calendarDay = receipt.snapshot.daily ? receipt.day : receipt.snapshot.scheduled ? receipt.snapshot.date : null;
            db.Execute("UPDATE " + table + " SET rewardDay=?,occurrenceDay=?,experience=?,fishCoins=?,tickets=?,calendarDay=?,sortKey=? WHERE id=?", receipt.rewardedLocalDay, receipt.day, receipt.experience, receipt.fishCoins, receipt.tickets, calendarDay, sortKey, row.id);
            PutHistoryIndex(db, receipt, row.sequence, sortKey);
            db.Execute("DELETE FROM cq_calendar_nodate WHERE id=?", receipt.id);
            if (!receipt.revoked && !receipt.snapshot.daily && (!receipt.snapshot.scheduled || !TaskService.TryDate(receipt.snapshot.date, out _)))
                db.Execute("INSERT INTO cq_calendar_nodate VALUES(?,?,?,?,?,?)", receipt.id, receipt.snapshot.title,
                    (receipt.snapshot.title ?? "") + " " + receipt.snapshot.description + " " + string.Join(" ", receipt.tagNames),
                    (int)receipt.snapshot.difficulty, JsonUtility.ToJson(new CalendarTagIds { ids = receipt.snapshot.tagIds }), row.sequence);
        }
        if (value is FocusSession session) {
            double eligible = session.eligibleSeconds > 0 ? session.eligibleSeconds : session.eligibleMinutes * 60d;
            bool successful = session.status == FocusSessionStatus.Completed || session.status == FocusSessionStatus.EndedEarly;
            bool wasSuccessful = existing != null && (existing.state == (int)FocusSessionStatus.Completed || existing.state == (int)FocusSessionStatus.EndedEarly);
            db.Execute("UPDATE cq_totals SET focusSeconds=focusSeconds+? WHERE id=1", (successful ? eligible : 0) - (wasSuccessful ? existing.eligibleSeconds : 0));
            db.Execute("DELETE FROM cq_focus_tags WHERE sessionId=?", session.id);
            foreach (string tag in (session.tagIds ?? new List<string>()).Where(t => !string.IsNullOrEmpty(t)).Distinct())
                db.Execute("INSERT INTO cq_focus_tags VALUES(?,?)", session.id, tag);
            db.Execute("UPDATE " + table + " SET rewardDay=?,eligibleSeconds=? WHERE id=?", session.rewardDay, session.eligibleSeconds > 0 ? session.eligibleSeconds : session.eligibleMinutes * 60d, row.id);
            FocusInterval(session,out var beginTicks,out var endTicks);
            db.Execute("UPDATE " + table + " SET beginTicks=?,endTicks=? WHERE id=?", beginTicks, endTicks, row.id);
        }
        if (value is QuestTask task) {
            if (!task.daily)
                db.Execute("UPDATE cq_Tasks SET state=CASE WHEN EXISTS(SELECT 1 FROM cq_Occurrences WHERE parentId=? AND day='once' AND state=1) THEN 2 ELSE 0 END WHERE id=?", task.id, task.id);
            db.Execute("DELETE FROM cq_task_tags WHERE taskId=?", task.id);
            db.Execute("DELETE FROM cq_subtasks WHERE taskId=?", task.id);
            for (int i = 0; i < (task.tagIds?.Count ?? 0); i++) db.Execute("INSERT INTO cq_task_tags VALUES(?,?,?)", task.id, task.tagIds[i], i);
            for (int i = 0; i < (task.subtasks?.Count ?? 0); i++) db.Execute("INSERT INTO cq_subtasks VALUES(?,?,?,?)", task.id, task.subtasks[i].id, i, task.subtasks[i].title);
        }
        if (value is TaskOccurrence occurrence && occurrence.day == "once")
            db.Execute("UPDATE cq_Tasks SET state=? WHERE id=? AND state!=1", occurrence.completed ? 2 : 0, occurrence.taskId);
    }
    internal static void WriteAll(SQLiteConnection db, PlayerData data)
    {
        foreach (var map in Mappings) {
            object value = map.Field == null ? data : typeof(PlayerData).GetField(map.Field).GetValue(data);
            if (value == null) continue;
            if (map.Singleton) Put(db, map, value, 0);
            else {
                long order = 0;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (object item in (IEnumerable)value) {
                    if (item == null || !ids.Add(map.Id(item))) throw new ArgumentException("Duplicate or null record in " + map.Kind);
                    Put(db, map, item, order++);
                }
            }
        }
    }
    // Full materialization is deliberately internal and used only for migration,
    // explicit transfer and verification, never for ordinary command execution.
    internal static PlayerData ReadAll(SQLiteConnection db)
    {
        var data = new PlayerData();
        foreach (var map in Mappings) {
            var rows = db.Query<Row>("SELECT * FROM " + Table(map.Kind) + " ORDER BY sequence");
            if (map.Kind == PlayerRecordKind.Profile) {
                if (rows.Count != 1) throw new ArgumentException("Missing player profile.");
                data = JsonUtility.FromJson<PlayerData>(rows[0].payload);
            } else if (map.Singleton) typeof(PlayerData).GetField(map.Field).SetValue(data, rows.Count == 0 ? null : JsonUtility.FromJson(rows.Single().payload, map.Type));
            else {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(map.Type));
                foreach (var row in rows) list.Add(JsonUtility.FromJson(row.payload, map.Type));
                typeof(PlayerData).GetField(map.Field).SetValue(data, list);
            }
        }
        return data;
    }
}

internal sealed class PlayerRepositoryException : Exception
{
    internal readonly PlayerStorageError Code;
    internal PlayerRepositoryException(PlayerStorageError code, string message) : base(message) { Code = code; }
}
