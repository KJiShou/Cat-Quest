using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class SqlitePlayerRepository
{
    static TaskView HistoryView(TaskCompletion item) => new TaskView {
        task = item.snapshot, history = item, tagNames = item.tagNames, tagColors = item.tagColors,
        occurrence = new TaskOccurrence { taskId = item.taskId, day = item.day, completed = true, rewardGranted = true, completedSubtaskIds = item.snapshot.subtasks.Select(s => s.id).ToList() }
    };
    static CalendarTaskItem DetachedCalendarItem(CalendarTaskItem item) => new CalendarTaskItem {
        kind = item.kind, date = item.date,
        view = new TaskView {
            task = item.view.task?.DetachedCopy(), occurrence = item.view.occurrence?.DetachedCopy(),
            history = item.view.history?.DetachedCopy(),
            tagNames = item.view.tagNames == null ? null : new List<string>(item.view.tagNames),
            tagColors = item.view.tagColors == null ? null : new List<string>(item.view.tagColors)
        }
    };
    public Task<OperationResult<PlayerHistoryPage>> QueryHistoryPageAsync(string cursor, string search, TaskDifficulty? difficulty, string[] tags)
    {
        string text = (search ?? "").Trim(); string[] selected = tags?.ToArray() ?? Array.Empty<string>();
        return Queue(() => {
            string sql = "SELECT h.* FROM cq_history_search h WHERE h.state=0";
            var args = new List<object>();
            if (difficulty.HasValue) { sql += " AND h.difficulty=?"; args.Add((int)difficulty.Value); }
            foreach (string tag in selected.Distinct(StringComparer.Ordinal)) {
                sql += " AND h.id IN (SELECT historyId FROM cq_history_tags WHERE tagId=?)"; args.Add(tag);
            }
            if (!string.IsNullOrEmpty(cursor)) {
                var parts = cursor.Split(':');
                if (parts.Length != 3 || !long.TryParse(parts[0], out var revision) || revision != Revision)
                    throw new PlayerCommandException("History changed. Refresh the list to continue.");
                byte[] key = Convert.FromBase64String(parts[1]); long sequence = long.Parse(parts[2], CultureInfo.InvariantCulture);
                sql += " AND (h.sortKey<? OR (h.sortKey=? AND h.sequence>?))";
                args.Add(key); args.Add(key); args.Add(sequence);
            }
            sql += " ORDER BY h.sortKey DESC,h.sequence";
            if (text.Length == 0) sql += " LIMIT 101";
            var page = new PlayerHistoryPage { TotalCount = database.ExecuteScalar<long>("SELECT taskCount FROM cq_totals WHERE id=1") };
            var matches = new List<SqlitePlayerSchema.HistoryIndexRow>();
            foreach (var row in database.DeferredQuery<SqlitePlayerSchema.HistoryIndexRow>(sql, args.ToArray())) {
                if (text.Length != 0 && row.searchText.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (matches.Count == 100) {
                    var last = matches[99];
                    page.NextCursor = Revision.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToBase64String(last.sortKey) + ":" + last.sequence.ToString(CultureInfo.InvariantCulture);
                    break;
                }
                matches.Add(row);
            }
            // Only this page's snapshots are materialized. Unicode substring search
            // deliberately uses the legacy managed comparer on compact metadata.
            var receipts = matches.Count == 0 ? new Dictionary<string, TaskCompletion>() : database.Query<SqlitePlayerSchema.Row>(
                "SELECT id,payload FROM cq_History WHERE id IN (" + string.Join(",", matches.Select(_=>"?")) + ")", matches.Select(r=>(object)r.id).ToArray())
                .ToDictionary(r=>r.id,r=>JsonUtility.FromJson<TaskCompletion>(r.payload));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HistoryPayloadsReadForVerification = receipts.Count;
#endif
            foreach (var row in matches) page.Rows.Add(HistoryView(receipts[row.id]));
            return page;
        });
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public int HistoryPayloadsReadForVerification { get; private set; }
#endif
    PlayerData SmallProfile()
    {
        var p = Query<PlayerData>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.Profile, Limit = 1 }).Single();
        p.settings = Query<AppSettings>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.Settings, Limit = 1 }).Single();
        p.account = Query<AccountPreferences>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.Account, Limit = 1 }).Single();
        p.tutorial = Query<TutorialProgress>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.Tutorial, Limit = 1 }).FirstOrDefault();
        p.tasks = new List<QuestTask>(); p.tags = new List<QuestTag>(); p.occurrences = new List<TaskOccurrence>(); p.history = new List<TaskCompletion>();
        p.focusSessions = new List<FocusSession>(); p.rewardDays = new List<RewardDay>(); p.battles = new List<BattleAttempt>();
        p.equipment = database.Query<SqlitePlayerSchema.Row>("SELECT payload FROM cq_Equipment ORDER BY sequence").Select(r=>JsonUtility.FromJson<EquipmentItem>(r.payload)).ToList();
        p.bossProgress = Query<BossProgress>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.BossProgress }).ToList();
        p.equipmentUpgrades = new List<EquipmentUpgradeReceipt>(); p.lootReceipts = new List<LootReceipt>();
        return p;
    }
    public Task<OperationResult<PlayerData>> QueryCalendarDataAsync(DateTime first, DateTime last, string today) => Queue(() => {
        if (last.Date < first.Date || (last.Date - first.Date).TotalDays > 366) throw new PlayerCommandException("Calendar range must be at most one year.");
        var p = SmallProfile();
        var tx = new Transaction(database, true);
        try { p.tasks = tx.ActiveTasks().ToList(); p.tags = tx.Tags().ToList(); }
        finally { tx.Active = false; }
        string start = TaskService.DateKey(first), end = TaskService.DateKey(last);
        p.occurrences = database.Query<SqlitePlayerSchema.Row>("SELECT payload,sequence FROM cq_Occurrences WHERE day>=? AND day<=? UNION ALL SELECT o.payload AS payload,o.sequence AS sequence FROM cq_Tasks t INDEXED BY cq_Tasks_state CROSS JOIN cq_Occurrences o ON o.parentId=t.id AND o.day='once' WHERE t.state=0 ORDER BY 2", start, end)
            .Select(r => JsonUtility.FromJson<TaskOccurrence>(r.payload)).ToList();
        p.history = database.Query<SqlitePlayerSchema.Row>("SELECT payload FROM cq_History WHERE state=0 AND calendarDay>=? AND calendarDay<=? ORDER BY sequence", start, end)
            .Select(r => JsonUtility.FromJson<TaskCompletion>(r.payload)).ToList();
        return p;
    });
    sealed class CalendarIndexRow
    {
        public string id { get; set; }
        public string title { get; set; }
        public string searchText { get; set; }
        public int difficulty { get; set; }
        public string tags { get; set; }
        public long sequence { get; set; }
        [SQLite.Ignore] public CalendarTaskItem Live { get; set; }
    }
    long noDateRevision = -1;
    string noDateSearch;
    TaskDifficulty? noDateDifficulty;
    string[] noDateTags;
    bool noDateCompleted;
    List<CalendarIndexRow> noDateIndex;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public int NoDateMetadataBuildsForVerification { get; private set; }
#endif
    void AdvanceNoDateCache(IReadOnlyCollection<PlayerRecordKind> changed,long previousRevision)
    {
        if(noDateIndex==null||noDateRevision!=previousRevision)return;
        if(changed.Any(kind=>kind==PlayerRecordKind.Tasks||kind==PlayerRecordKind.Occurrences||kind==PlayerRecordKind.History||kind==PlayerRecordKind.Tags))return;
        noDateRevision=Revision;
    }
    public Task<OperationResult<PlayerCalendarPage>> QueryNoDatePageAsync(string cursor, string search, TaskDifficulty? difficulty, string[] tags, bool showCompleted)
    {
        string text = (search ?? "").Trim(); string[] selected = tags?.ToArray() ?? Array.Empty<string>();
        return Queue(() => {
            int offset = 0;
            if (!string.IsNullOrEmpty(cursor)) {
                var parts = cursor.Split(':');
                if (parts.Length != 2 || !long.TryParse(parts[0], out var version) || version != Revision || !int.TryParse(parts[1], out offset) || offset < 0)
                    throw new PlayerCommandException("Calendar changed. Refresh the list to continue.");
            }
            if (noDateRevision != Revision || noDateSearch != text || noDateDifficulty != difficulty || noDateCompleted != showCompleted || noDateTags == null || !noDateTags.SequenceEqual(selected)) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                NoDateMetadataBuildsForVerification++;
#endif
                var tx = new Transaction(database, true);
                List<QuestTask> tasks; List<QuestTag> catalog; List<TaskOccurrence> occurrences;
                try { tasks = tx.ActiveTasks().ToList(); catalog = tx.Tags().ToList(); occurrences = tx.CurrentOccurrences("").ToList(); }
                finally { tx.Active = false; }
                var once = occurrences.Where(o => o.day == "once").ToDictionary(o => o.taskId, StringComparer.Ordinal);
                var rows = new List<CalendarIndexRow>();
                long order = 0;
                foreach (var task in tasks.Where(t => !t.daily && (!t.scheduled || !TaskService.TryDate(t.date, out _)))) {
                    if (!once.TryGetValue(task.id, out var occurrence)) occurrence = new TaskOccurrence { taskId = task.id, day = "once" };
                    if (occurrence.completed) continue;
                    var tagRecords = catalog.Where(t => task.tagIds.Contains(t.id)).ToArray();
                    var view = new TaskView { task = task, occurrence = occurrence, tagNames = tagRecords.Select(t => t.name).ToList(), tagColors = tagRecords.Select(t => t.colorHex).ToList() };
                    string searchable = (task.title ?? "") + " " + task.description + " " + string.Join(" ", view.tagNames);
                    if (difficulty.HasValue && task.difficulty != difficulty.Value || !selected.All(task.tagIds.Contains) || text.Length != 0 && searchable.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    rows.Add(new CalendarIndexRow { id = task.id, title = task.title, sequence = order++, Live = new CalendarTaskItem { view = view, kind = CalendarTaskKind.Task } });
                }
                // Compare with the same managed comparer as legacy Calendar. SQLite's
                // NOCASE and uppercase byte keys differ for some Unicode on Mono.
                // Cache compact metadata, never all completion snapshots, per revision.
                if (showCompleted)
                    foreach (var row in database.DeferredQuery<CalendarIndexRow>("SELECT * FROM cq_calendar_nodate ORDER BY sequence")) {
                        if (difficulty.HasValue && row.difficulty != (int)difficulty.Value || text.Length != 0 && row.searchText.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (selected.Length != 0 && !selected.All(JsonUtility.FromJson<SqlitePlayerSchema.CalendarTagIds>(row.tags).ids.Contains)) continue;
                        rows.Add(row);
                    }
                noDateIndex = rows.OrderBy(r => r.Live == null).ThenBy(r => r.title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.sequence).ToList();
                noDateRevision = Revision; noDateSearch = text; noDateDifficulty = difficulty; noDateTags = selected; noDateCompleted = showCompleted;
            }
            var page = new PlayerCalendarPage { TotalCount = noDateIndex.Count };
            var batch = noDateIndex.Skip(offset).Take(100).ToArray();
            var ids = batch.Where(r => r.Live == null).Select(r => r.id).ToArray();
            var receipts = ids.Length == 0 ? new Dictionary<string, TaskCompletion>() : database.Query<SqlitePlayerSchema.Row>(
                "SELECT id,payload FROM cq_History WHERE id IN (" + string.Join(",", ids.Select(_ => "?")) + ")", ids.Cast<object>().ToArray())
                .ToDictionary(r => r.id, r => JsonUtility.FromJson<TaskCompletion>(r.payload));
            foreach (var row in batch) page.Rows.Add(row.Live != null ? DetachedCalendarItem(row.Live) : new CalendarTaskItem { view = HistoryView(receipts[row.id]), kind = CalendarTaskKind.History });
            if (offset + batch.Length < noDateIndex.Count) page.NextCursor = Revision.ToString(CultureInfo.InvariantCulture) + ":" + (offset + batch.Length).ToString(CultureInfo.InvariantCulture);
            return page;
        });
    }
    sealed class FocusAggregateRow
    {
        public int bucket { get; set; }
        public string tagId { get; set; }
        public double seconds { get; set; }
        public int sessions { get; set; }
        public int multiple { get; set; }
        public long firstSequence { get; set; }
    }
    public Task<OperationResult<FocusStatistics.Result>> QueryFocusStatisticsAsync(DateTime date, FocusStatisticsPeriod period, TimeZoneInfo zone) => Queue(() => {
        if (zone == null || !Enum.IsDefined(typeof(FocusStatisticsPeriod), period)) throw new ArgumentException("Invalid statistics period.");
        // Keep .NET timezone/DST rules and labels identical to the legacy projection.
        var result = FocusStatistics.Query(Array.Empty<FocusSession>(), Array.Empty<QuestTag>(), date, period, zone);
        int count = result.buckets.Length;
        var ticks = new long[count + 1];
        for (int i = 0; i <= count; i++) {
            var local = period == FocusStatisticsPeriod.Day ? result.start.AddHours(i)
                : period == FocusStatisticsPeriod.Year ? result.start.AddMonths(i) : result.start.AddDays(i);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            ticks[i] = (zone.IsAmbiguousTime(local)
                ? DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc)
                : TimeZoneInfo.ConvertTimeToUtc(local, zone)).Ticks;
        }
        var args = new List<object>();
        var intervals = new List<string>();
        for (int i = -1; i < count; i++) {
            intervals.Add("(?,?,?)");args.Add(i);args.Add(ticks[i < 0 ? 0 : i]);args.Add(ticks[i < 0 ? count : i + 1]);
        }
        args.Add(ticks[0]);args.Add(ticks[count]);
        args.Add((int)FocusSessionStatus.Completed);args.Add((int)FocusSessionStatus.EndedEarly);
        // Metadata is calculated with the same elapsed/eligible/minutes fallback on write.
        // Aggregate integer tick differences before converting each overlap to seconds;
        // no history payloads or session-sized managed collections are materialized.
        string common = "WITH buckets(bucket,lo,hi) AS (VALUES " + string.Join(",", intervals) + "), " +
            "eligible AS (SELECT id,sequence,beginTicks,endTicks FROM cq_FocusSessions WHERE endTicks>? AND beginTicks<? AND endTicks>beginTicks AND state IN (?,?)) ";
        var totals = database.Query<FocusAggregateRow>(common +
            "SELECT b.bucket,COALESCE(SUM((MIN(s.endTicks,b.hi)-MAX(s.beginTicks,b.lo))/10000000.0),0) AS seconds," +
            "COUNT(s.id) AS sessions,COALESCE(MAX((SELECT COUNT(*) FROM cq_focus_tags t WHERE t.sessionId=s.id)>1),0) AS multiple " +
            "FROM buckets b LEFT JOIN eligible s ON s.endTicks>b.lo AND s.beginTicks<b.hi AND b.hi>b.lo GROUP BY b.bucket", args.ToArray());
        foreach (var row in totals) {
            if (row.bucket < 0) { result.seconds=row.seconds;result.sessions=row.sessions;result.legacyMultipleTags=row.multiple!=0; }
            else result.buckets[row.bucket]=row.seconds;
        }
        var tagArgs = new List<object>(args) { FocusStatistics.Untagged };
        var rows = database.Query<FocusAggregateRow>(common +
            ", tagged AS (SELECT s.*,COALESCE(t.tagId,?) AS tagId FROM eligible s LEFT JOIN cq_focus_tags t ON t.sessionId=s.id) " +
            "SELECT b.bucket,s.tagId,SUM((MIN(s.endTicks,b.hi)-MAX(s.beginTicks,b.lo))/10000000.0) AS seconds,MIN(s.sequence) AS firstSequence " +
            "FROM buckets b JOIN tagged s ON s.endTicks>b.lo AND s.beginTicks<b.hi AND b.hi>b.lo GROUP BY b.bucket,s.tagId", tagArgs.ToArray());
        var summaries=rows.Where(r=>r.bucket<0).ToArray();
        var firstSequences=summaries.Select(r=>r.firstSequence).Distinct().ToArray();
        // Deleted tag names come from the first overlapping session in original order,
        // not the first session ever recorded. Read at most one payload per result tag.
        var firstSessions=firstSequences.Length==0?new Dictionary<long,FocusSession>():database.Query<SqlitePlayerSchema.Row>(
            "SELECT sequence,payload FROM cq_FocusSessions WHERE sequence IN ("+string.Join(",",firstSequences.Select(_=>"?"))+")",firstSequences.Cast<object>().ToArray())
            .ToDictionary(r=>r.sequence,r=>JsonUtility.FromJson<FocusSession>(r.payload));
        var catalog=database.Query<SqlitePlayerSchema.Row>("SELECT payload FROM cq_Tags ORDER BY sequence")
            .Select(r=>JsonUtility.FromJson<QuestTag>(r.payload)).ToDictionary(t=>t.id??"",StringComparer.Ordinal);
        var series=new Dictionary<string,FocusStatistics.Series>(StringComparer.Ordinal);
        foreach(var row in summaries.OrderBy(r=>r.firstSequence).ThenBy(r=>firstSessions[r.firstSequence].tagIds?.IndexOf(r.tagId)??-1)) {
            var session=firstSessions[row.firstSequence];catalog.TryGetValue(row.tagId,out var current);
            int index=session.tagIds?.IndexOf(row.tagId)??-1;
            string oldName=index>=0&&index<(session.tagNames?.Count??0)?session.tagNames[index]:null;
            series.Add(row.tagId,new FocusStatistics.Series { id=row.tagId,name=row.tagId==FocusStatistics.Untagged?"No tag":current?.name??oldName??"Deleted tag",
                color=current?.colorHex??"#5CA6CE",seconds=row.seconds,buckets=new double[count] });
        }
        foreach(var row in rows)if(row.bucket>=0)series[row.tagId].buckets[row.bucket]=row.seconds;
        result.tags=series.Values.OrderByDescending(t=>t.seconds).ThenBy(t=>t.name,StringComparer.Ordinal).ToList();
        return result;
    });
    public Task<OperationResult<List<QuestTag>>> QueryFocusStatisticsTagCatalogAsync() => Queue(() => {
        var tags = database.Query<SqlitePlayerSchema.Row>("SELECT payload FROM cq_Tags ORDER BY sequence").Select(r => JsonUtility.FromJson<QuestTag>(r.payload)).ToDictionary(t => t.id, StringComparer.Ordinal);
        foreach (var row in database.Query<SqlitePlayerSchema.Row>("SELECT DISTINCT tagId AS id FROM cq_focus_tags"))
            if (!tags.ContainsKey(row.id)) tags[row.id] = new QuestTag { id = row.id, colorHex = "#5CA6CE" };
        return tags.Values.OrderBy(t => t.id, StringComparer.Ordinal).ToList();
    });
    public Task<OperationResult<AccountAggregate>> QueryAccountAggregateAsync(string uid, string name) => Queue(() => {
        var result = AccountAggregate.From(SmallProfile(), uid, name);
        result.totalTasksCompleted = database.ExecuteScalar<long>("SELECT taskCount FROM cq_totals WHERE id=1");
        double seconds = database.ExecuteScalar<double>("SELECT focusSeconds FROM cq_totals WHERE id=1");
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds / 60 >= long.MaxValue) throw new PlayerCommandException("Focus statistics exceed the supported range.");
        result.totalFocusMinutes = (long)Math.Floor(seconds / 60);
        return result;
    });
}
