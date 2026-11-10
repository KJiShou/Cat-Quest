using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

// Invoked by the root's isolated Play runner. No runner, network or authored asset writes.
public static class RemainingPerformanceVerificationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly DateTime Now = new DateTime(2026, 11, 9, 12, 0, 0);
    const string Baseline = "Temp/DataPerformanceBaseline-20261109/query-signatures.json";
    [Serializable] sealed class Metric
    {
        public string operation;
        public int activeTasks, days, occurrences, history, focusSessions, warmup, repeats;
        public double millisecondsPerCall;
        public long bytesPerCall, managedHeapDelta;
        public bool allocationCounterAvailable;
        public int gen0Collections;
    }
    [Serializable] sealed class Report { public string phase, utc, unity; public List<Metric> metrics = new List<Metric>(); }
    [Serializable] sealed class Signatures { public List<string> values = new List<string>(); }
    sealed class Clock : ITaskClock { public DateTime LocalNow => Now; public DateTime UtcNow => DateTime.SpecifyKind(Now, DateTimeKind.Utc); }
    sealed class MemoryStore : IPlayerDataStore
    {
        public PlayerData Data { get; private set; }
        public MemoryStore(PlayerData data) { Data = data; }
        public bool TrySave(PlayerData candidate, out string error) { Data = candidate; error = null; return true; }
    }
    static object sink;
    static void Measure(Report report, string operation, PlayerData data, int days, Action action, int repeats = 3, int warmup = 1)
    {
        for (int i = 0; i < warmup; i++) action();
        var timer = new Stopwatch();
        long heap = GC.GetTotalMemory(false);
        int collections = GC.CollectionCount(0);
        long start = GC.GetAllocatedBytesForCurrentThread();
        timer.Start();
        for (int i = 0; i < repeats; i++) action();
        timer.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        report.metrics.Add(new Metric { operation = operation, activeTasks = data.tasks.Count, days = days,
            occurrences = data.occurrences.Count, history = data.history.Count, focusSessions = data.focusSessions.Count,
            warmup = warmup, repeats = repeats, millisecondsPerCall = timer.Elapsed.TotalMilliseconds / repeats,
            allocationCounterAvailable = start > 0 || bytes > 0, bytesPerCall = start > 0 || bytes > 0 ? bytes / repeats : -1,
            managedHeapDelta = GC.GetTotalMemory(false) - heap, gen0Collections = GC.CollectionCount(0) - collections });
    }
    static PlayerData Fixture(int active, int days)
    {
        var data = new PlayerData { fishCoins = 123, tickets = 9, tagsInitialized = true, tutorial = new TutorialProgress { version = 1, dismissed = true } };
        for (int i = 0; i < 12; i++) data.tags.Add(new QuestTag { id = "tag-" + i, name = "Subject " + i });
        for (int i = 0; i < active; i++)
        {
            var task = new QuestTask { id = "active-" + i, title = "Task " + i.ToString("D4"), description = "Research and revision",
                daily = i % 4 == 0, scheduled = i % 3 != 0, date = TaskService.DateKey(Now.AddDays(i % 31 - 15)),
                difficulty = (TaskDifficulty)(i % 3), tagIds = new List<string> { "tag-" + i % 12 } };
            if (task.daily) task.date = TaskService.DateKey(Now.AddDays(-days));
            task.subtasks.Add(new QuestSubtask { id = "sub-" + i, title = "Review" });
            data.tasks.Add(task);
            data.occurrences.Add(new TaskOccurrence { taskId = task.id, day = task.daily ? TaskService.DateKey(Now) : "once" });
        }
        for (int d = 1; d <= days; d++)
        {
            string day = TaskService.DateKey(Now.AddDays(-d));
            for (int n = 0; n < 12; n++) data.occurrences.Add(new TaskOccurrence { taskId = "active-" + (n * 4), day = day, completed = n < 8, rewardGranted = n < 8 });
            for (int n = 0; n < 8; n++)
            {
                var task = new QuestTask { id = "old-" + d + "-" + n, title = "Historic task " + n, daily = n % 2 == 0,
                    scheduled = n % 3 != 0, date = day, difficulty = (TaskDifficulty)(n % 3), tagIds = new List<string> { "tag-" + n } };
                data.history.Add(new TaskCompletion { id = "receipt-" + d + "-" + n, taskId = task.id, day = task.daily ? day : "once", snapshot = task,
                    completedUtc = Now.AddDays(-d).ToUniversalTime().ToString("O"), completedLocalDay = day, rewardedLocalDay = day,
                    tagNames = new List<string> { "Subject " + n }, tagColors = new List<string> { TagColors.DefaultHex }, revoked = d % 23 == 0 });
            }
            for (int n = 0; n < 4; n++) data.focusSessions.Add(new FocusSession { id = "focus-" + d + "-" + n,
                status = n == 3 ? FocusSessionStatus.Cancelled : n == 2 ? FocusSessionStatus.EndedEarly : FocusSessionStatus.Completed,
                completedUtc = DateTime.SpecifyKind(Now.AddDays(-d).AddHours(n), DateTimeKind.Utc).ToString("O"), elapsedSeconds = n == 2 ? 90 : 1500,
                tagIds = new List<string> { "tag-" + d % 12 }, tagNames = new List<string> { "Subject " + d % 12 } });
        }
        PlayerDataStore.Normalize(data);
        return data;
    }
    static string Fingerprint(IEnumerable<TaskView> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows) text.Append(row.Key).Append('|').Append(JsonUtility.ToJson(row.task)).Append('|')
            .Append(JsonUtility.ToJson(row.occurrence)).Append('|').Append(string.Join(",", row.tagNames)).Append('|').Append(string.Join(",", row.tagColors)).Append('\n');
        return Hash(text.ToString());
    }
    static string Fingerprint(TaskCalendarResult calendar)
    {
        var text = new StringBuilder();
        foreach (var day in calendar.days.OrderBy(p => p.Key)) text.Append(TaskService.DateKey(day.Key)).Append(':').Append(day.Value.missed)
            .Append(':').Append(string.Join(",", day.Value.items.Select(i => i.Key))).Append(':').Append(Fingerprint(day.Value.items.Select(i => i.view))).Append('\n');
        text.Append(Fingerprint(calendar.noDate.Select(i => i.view)));
        return Hash(text.ToString());
    }
    static string Hash(string value)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
    }
    static string Fingerprint(FocusStatistics.Result result)
    {
        var text = new StringBuilder(result.start.ToString("O")).Append('|').Append(result.end.ToString("O"))
            .Append('|').Append(result.sessions).Append('|').Append(result.seconds.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(result.legacyMultipleTags)
            .Append('|').Append(string.Join(",", result.labels));
        foreach (double seconds in result.buckets) text.Append('|').Append(seconds.ToString("R", CultureInfo.InvariantCulture));
        foreach (var tag in result.tags) {
            text.Append('\n').Append(tag.id).Append('|').Append(tag.name).Append('|').Append(tag.color).Append('|').Append(tag.seconds.ToString("R", CultureInfo.InvariantCulture));
            foreach (double seconds in tag.buckets) text.Append('|').Append(seconds.ToString("R", CultureInfo.InvariantCulture));
        }
        return Hash(text.ToString());
    }
    public static IEnumerable<double> Run(bool before, string output, Action<bool, string> check)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated) == Path.GetFullPath(GameDataManager.EnsureInstance().SavePath), "Performance UI runs only with the root's isolated save");
        var navigation = Object.FindFirstObjectByType<MainMenuNavigation>();
        check(navigation, "Performance UI requires the authored navigation");
        foreach (var page in new[] { MainMenuPage.Tasks, MainMenuPage.Focus, MainMenuPage.Chat }) { check(navigation.ShowPage(page), "Performance fixture opens " + page); yield return .2; }
        var report = new Report { phase = before ? "before" : "after", utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion };
        var signatures = new Signatures();
        string directory = Path.Combine(output, "performance-" + report.phase); Directory.CreateDirectory(directory);
        foreach (var scale in new[] { (active: 100, days: 365), (active: 1000, days: 1095) })
        {
            var data = Fixture(scale.active, scale.days);
            if (!before) {
                string original = JsonUtility.ToJson(data);
                var detached = PlayerDataStore.Copy(data);
                check(JsonUtility.ToJson(detached) == original, "Optimized full copy retains every serialized value at scale " + scale.active);
                detached.tasks[0].title = "changed";
                detached.tasks[0].tagIds.Clear(); detached.tasks[0].subtasks[0].title = "changed";
                detached.tags[0].name = "changed";
                detached.occurrences[0].completedSubtaskIds.Add("changed");
                detached.history[0].snapshot.title = "changed";
                detached.history[0].tagNames.Clear(); detached.history[0].tagColors.Clear();
                detached.focusSessions[0].tagIds.Clear(); detached.focusSessions[0].tagNames.Clear();
                check(JsonUtility.ToJson(data) == original, "Mutating copied task, subtask, occurrence, receipt, tag and Focus lists leaves authoritative data untouched");
            }
            var service = new TaskService(new MemoryStore(data), new Clock());
            if (!before) {
                var browse = (PlayerData)typeof(TaskService).GetMethod("BrowseSnapshot", Hidden).Invoke(service, new object[] { false });
                check(browse.history.Count == 0 && browse.occurrences.All(o => o.day == "once" || o.day == TaskService.DateKey(Now)), "Ordinary browsing excludes historical payloads");
                foreach (TaskTab tab in Enum.GetValues(typeof(TaskTab))) {
                    int expected = tab == TaskTab.Tags ? data.tags.Count : service.Query(tab).Count;
                    if (tab == TaskTab.Todo) expected += service.Query(TaskTab.Daily).Count(v => !v.Completed);
                    int actual = (int)typeof(TaskService).GetMethod("BrowseCount", Hidden).Invoke(service, new object[] { browse, tab });
                    check(actual == expected, "Browse tab count matches full query: " + tab);
                }
                check(service.Query(TaskTab.History).Select(v => v.history.id).SequenceEqual(data.history.Where(h => !h.revoked).OrderByDescending(h => h.completedUtc).Select(h => h.id)), "History collation preserves original order and stable ties");
                browse.tasks[0].title = "Detached browse mutation";
                check(data.tasks[0].title != browse.tasks[0].title, "Browse task projection stays detached");
            }
            Measure(report, "Snapshot.Copy", data, scale.days, () => sink = service.Snapshot);
            Measure(report, "Serialize.Pretty", data, scale.days, () => sink = JsonUtility.ToJson(data, true));
            yield return 0;
            foreach (TaskTab tab in Enum.GetValues(typeof(TaskTab)))
            {
                Measure(report, "Tasks." + tab, data, scale.days, () => sink = service.Query(tab));
                signatures.values.Add(scale.active + ":" + tab + ":" + Fingerprint(service.Query(tab)));
                yield return 0;
            }
            Measure(report, "Tasks.Search", data, scale.days, () => sink = service.Query(TaskTab.Today, "research", TaskDifficulty.Normal, new[] { "tag-1" }));
            signatures.values.Add(scale.active + ":Search:" + Fingerprint(service.Query(TaskTab.Today, "research", TaskDifficulty.Normal, new[] { "tag-1" })));
            var first = new DateTime(Now.Year, Now.Month, 1);
            Measure(report, "Calendar.Month", data, scale.days, () => sink = service.QueryCalendar(first, first.AddMonths(1).AddDays(-1)));
            signatures.values.Add(scale.active + ":Calendar:" + Fingerprint(service.QueryCalendar(first, first.AddMonths(1).AddDays(-1))));
            signatures.values.Add(scale.active + ":Calendar.Filtered:" + Fingerprint(service.QueryCalendar(first.AddMonths(-1), first.AddDays(-1), "Historic", TaskDifficulty.Easy, new[] { "tag-0" })));
            yield return 0;
            foreach (FocusStatisticsPeriod period in Enum.GetValues(typeof(FocusStatisticsPeriod)))
            {
                Measure(report, "Statistics.Model." + period, data, scale.days, () => sink = FocusStatistics.Query(data.focusSessions, data.tags, Now, period, TimeZoneInfo.Utc));
                signatures.values.Add(scale.active + ":Statistics:" + period + ":" + Fingerprint(FocusStatistics.Query(data.focusSessions, data.tags, Now.AddDays(-1), period, TimeZoneInfo.Utc)));
            }
            string path = Path.GetFullPath(Path.Combine(directory, "scale-" + scale.active + ".json"));
            var store = new PlayerDataStore(path);
            check(store.TrySave(PlayerDataStore.Copy(data), out _), "Performance fixture saved only to isolated output");
            var candidate = PlayerDataStore.Copy(store.Data);
            check(store.TrySave(candidate, out _), "Stage measurement establishes both primary and backup");
            Measure(report, "Save.Normalize", data, scale.days, () => PlayerDataStore.Normalize(candidate));
            string serialized = JsonUtility.ToJson(candidate);
            Measure(report, "Save.SerializeCompact", data, scale.days, () => sink = JsonUtility.ToJson(candidate));
            byte[] encoded = Encoding.UTF8.GetBytes(serialized);
            Measure(report, "Save.Encode", data, scale.days, () => sink = Encoding.UTF8.GetBytes(serialized));
            var validate = typeof(PlayerDataStore).GetMethod("TryRead", Hidden);
            Measure(report, "Save.ValidateBoth", data, scale.days, () => {
                var both = typeof(PlayerDataStore).GetMethod("ValidateFiles", Hidden);
                if (both != null) both.Invoke(store, new object[] { false, 0, 0 });
                else {
                    validate.Invoke(store, new object[] { path, null, 0, true });
                    validate.Invoke(store, new object[] { path + ".bak", null, 0, true });
                }
            });
            Measure(report, "Store.TrySave", data, scale.days, () => { if (!store.TrySave(candidate, out var error)) throw new InvalidOperationException(error); });
            if (!before) {
                long retained = 0;
                foreach (string name in new[] { "primaryValidation", "backupValidation" }) {
                    var cache = typeof(PlayerDataStore).GetField(name, Hidden).GetValue(store);
                    retained += ((byte[])cache.GetType().GetField("content").GetValue(cache)).LongLength;
                }
                File.WriteAllText(Path.Combine(directory, "validation-cache-" + scale.active + ".txt"), "Retained exact file bytes: " + retained + "\nPrevious UTF16 cache bytes: " + (File.ReadAllText(path).Length + File.ReadAllText(path + ".bak").Length) * 2L);
            }
            var persistedService = new TaskService(store, new Clock());
            Measure(report, "Commit.TagRename", data, scale.days, () => {
                if (!persistedService.SaveTag("tag-0", "Subject 0", TagColors.DefaultHex, out _)) throw new InvalidOperationException(persistedService.LastError);
            });
            check(new PlayerDataStore(path).Data.history.Count == data.history.Count, "Save/reload retains full history at scale " + scale.active);
            check(service.Query(TaskTab.Today).All(v => v.task != data.tasks.FirstOrDefault(t => t.id == v.task.id)), "Task query results do not expose authoritative task objects");
            yield return 0;
            if (scale.active == 1000) MeasureUI(report, data, service, before, check);
            Write(report, directory);
            yield return 0;
        }
        if (before) { Directory.CreateDirectory(Path.GetDirectoryName(Baseline)); File.WriteAllText(Baseline, JsonUtility.ToJson(signatures, true)); }
        else {
            check(File.Exists(Baseline), "Current-run before query signatures are available");
            if (File.Exists(Baseline)) check(JsonUtility.FromJson<Signatures>(File.ReadAllText(Baseline)).values.SequenceEqual(signatures.values), "All task/filter/calendar/statistics results match the current before baseline");
        }
        File.WriteAllText(Path.Combine(directory, "query-signatures.json"), JsonUtility.ToJson(signatures, true));
        SaveRegressions(directory, check);
        if (!before)
        {
            LargeSaveRegressions(directory, check);
            ReplacementRetryRegressions(directory, check);
            var receipts = new[] { "2026-01-01T00:00:00Z", null, "", "2026-01-01T00:00:00Z", "2026-01-01T00:00:00+08:00", "2025-12-31T23:59:59.999Z" }
                .Select((stamp, i) => new TaskCompletion { id = i.ToString(), completedUtc = stamp }).ToArray();
            var sorted = (IEnumerable<TaskCompletion>)typeof(TaskService).GetMethod("OrderHistory", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { receipts });
            check(sorted.Select(h => h.id).SequenceEqual(receipts.OrderByDescending(h => h.completedUtc).Select(h => h.id)), "History order retains null/empty stamps, offsets, fractions and stable duplicates");
            ProjectionRegressions(check);
            typeof(FocusStatisticsVerification).GetMethod("DataChecks", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            check(true, "Existing Focus Statistics time/status/duplicate/reload/DST checks pass");
            WebVerification.Run();
            check(true, "Existing browser-store failure/retry/duplicate/stale-writer/recovery checks pass");
        }
        Write(report, directory); sink = null;
    }
    static void MeasureUI(Report report, PlayerData data, TaskService service, bool before, Action<bool, string> check)
    {
        var tasks = Object.FindFirstObjectByType<TaskSystemUI>(FindObjectsInactive.Include);
        check(tasks && !tasks.calendar.Active, "Authored Tasks list exists and Calendar is closed for refresh measurement");
        if (tasks)
        {
            var serviceField = typeof(TaskSystemUI).GetField("<Service>k__BackingField", Hidden); var original = serviceField.GetValue(tasks);
            var tabField = typeof(TaskSystemUI).GetField("<CurrentTab>k__BackingField", Hidden); var oldTab = tabField.GetValue(tasks);
            var selectedField = typeof(TaskSystemUI).GetField("<SelectedKey>k__BackingField", Hidden); var oldSelected = selectedField.GetValue(tasks);
            var difficultyField = typeof(TaskSystemUI).GetField("difficultyFilter", Hidden); var oldDifficulty = difficultyField.GetValue(tasks);
            var filters = (HashSet<string>)typeof(TaskSystemUI).GetField("tagFilters", Hidden).GetValue(tasks); var oldFilters = filters.ToArray();
            string search = tasks.search.text;
            try {
                serviceField.SetValue(tasks, service); tabField.SetValue(tasks, TaskTab.Today); selectedField.SetValue(tasks, null);
                difficultyField.SetValue(tasks, null); filters.Clear(); tasks.search.SetTextWithoutNotify("");
                Measure(report, "UI.Tasks.Refresh", data, 1095, tasks.Refresh, 3, 1);
                check(tasks.fishCoins.text == data.fishCoins.ToString() && tasks.tickets.text == data.tickets.ToString(), "Task projection preserves displayed currency balances");
            } finally {
                serviceField.SetValue(tasks, original); tabField.SetValue(tasks, oldTab); selectedField.SetValue(tasks, oldSelected);
                difficultyField.SetValue(tasks, oldDifficulty); filters.Clear(); filters.UnionWith(oldFilters); tasks.search.SetTextWithoutNotify(search); tasks.Refresh();
            }
        }
        var tabs = Object.FindFirstObjectByType<TaskTabsLayout>(FindObjectsInactive.Include);
        check(tabs, "Authored Tasks tab component exists for idle allocation measurement");
        if (tabs) { Canvas.ForceUpdateCanvases(); Measure(report, "UI.Tabs.IdleApply", data, 1095, tabs.Apply, 1000, 3); }
        var stats = Object.FindFirstObjectByType<FocusStatisticsUI>(FindObjectsInactive.Include);
        check(stats, "Authored Focus Statistics component exists for refresh measurement");
        if (stats)
        {
            var field = typeof(FocusStatisticsUI).GetField("service", Hidden); var original = field.GetValue(stats);
            var date = typeof(FocusStatisticsUI).GetField("<Date>k__BackingField", Hidden); var oldDate = date.GetValue(stats);
            try {
                field.SetValue(stats, service); date.SetValue(stats, Now.AddDays(-1));
                Measure(report, "UI.Statistics.Refresh", data, 1095, stats.Refresh, 3, 2);
                var ids = stats.tagContent.GetComponentsInChildren<UnityEngine.UI.Button>(true).Where(b => b != stats.tagTemplate && b.gameObject.activeSelf).Select(b => b.GetInstanceID()).ToArray();
                stats.Refresh();
                var next = stats.tagContent.GetComponentsInChildren<UnityEngine.UI.Button>(true).Where(b => b != stats.tagTemplate && b.gameObject.activeSelf).Select(b => b.GetInstanceID()).ToArray();
                if (!before)
                {
                    check(ids.Length > 0 && ids.SequenceEqual(next), "Statistics refresh reuses tag button identities");
                    var row = stats.tagContent.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b => b != stats.tagTemplate && b.gameObject.activeSelf);
                    string id = row.name; int identity = row.GetInstanceID();
                    row.onClick.Invoke(); check(stats.SelectedTag == id, "Reused Statistics row still selects its own tag");
                    date.SetValue(stats, Now.AddYears(-10)); stats.Refresh();
                    check(!row.gameObject.activeSelf && stats.SelectedTag == null, "Empty period hides pooled rows and clears missing selection");
                    date.SetValue(stats, Now.AddDays(-1)); stats.Refresh();
                    check(row.gameObject.activeSelf && row.GetInstanceID() == identity, "Returning to a period reuses its tag button");
                    check(service.SaveTag(id, "Renamed statistics tag", "#E76F51", out _), "Statistics catalog rename commits"); stats.Refresh();
                    check(row.GetComponentInChildren<TMPro.TMP_Text>().text.StartsWith("Renamed statistics tag"), "Statistics cache invalidates after a catalog change");
                    check(service.DeleteTag(id), "Statistics tag deletion commits"); stats.Refresh();
                    check(row.gameObject.activeSelf && stats.Data.tags.Any(t => t.id == id) && !row.GetComponentInChildren<TMPro.TMP_Text>().text.StartsWith("Renamed statistics tag"), "Deleted tags retain historical sessions and snapshot names");
                }
            } finally { field.SetValue(stats, original); date.SetValue(stats, oldDate); if (original != null && (DateTime)oldDate != default) stats.Refresh(); }
        }
        var page = Object.FindFirstObjectByType<AiChatPage>(FindObjectsInactive.Include);
        var client = ChatGptClient.Instance;
        check(page && client && client.Chat != null, "Isolated AI component exists for input measurement");
        if (!page || !client || client.Chat == null) return;
        var sessionField = typeof(AiChatPage).GetField("session", Hidden); var oldSession = sessionField.GetValue(page);
        var clientField = typeof(AiChatPage).GetField("client", Hidden); var oldClient = clientField.GetValue(page);
        var rendered = (List<AiChatMessage>)typeof(AiChatPage).GetField("renderedMessages", Hidden).GetValue(page);
        var oldRendered = rendered.ToArray(); var oldMessages = client.Chat.History.messages.ToArray(); string oldInput = page.input.text;
        try {
            sessionField.SetValue(page, client.Chat); clientField.SetValue(page, client);
            client.Chat.History.messages.Clear(); rendered.Clear();
            for (int i = 0; i < 1000; i++) { var message = new AiChatMessage { role = "user", text = "Historical message " + i }; client.Chat.History.messages.Add(message); rendered.Add(message); }
            page.input.SetTextWithoutNotify("Draft text");
            Measure(report, "UI.AI.Input.1000Messages", data, 1095, () => page.input.onValueChanged.Invoke(page.input.text), 100, 3);
            check(client.Chat.History.messages.Count == 1000 && rendered.Count == 1000, "Typing does not append or remove chat history");
        } finally {
            client.Chat.History.messages.Clear(); client.Chat.History.messages.AddRange(oldMessages); rendered.Clear(); rendered.AddRange(oldRendered);
            sessionField.SetValue(page, oldSession); clientField.SetValue(page, oldClient); page.input.SetTextWithoutNotify(oldInput);
        }
    }
    static void ProjectionRegressions(Action<bool, string> check)
    {
        var source = Fixture(12, 3); var service = new TaskService(new MemoryStore(source), new Clock());
        string original = JsonUtility.ToJson(source);
        var snapshot = service.TaskSnapshot;
        check(snapshot.fishCoins == source.fishCoins && snapshot.tickets == source.tickets && snapshot.history.Count == source.history.Count, "Task snapshot retains currency and complete task history");
        snapshot.tasks[0].title = "changed copy"; snapshot.tags[0].name = "changed copy";
        snapshot.occurrences[0].completed = true; snapshot.history[0].snapshot.title = "changed copy";
        var row = service.Query(TaskTab.Today).First(); row.task.title = "changed view"; row.occurrence.completedSubtaskIds.Add("changed view");
        var calendar = service.QueryCalendar(Now.Date.AddDays(-3), Now.Date.AddDays(3));
        calendar.days.Values.SelectMany(day => day.items).First().view.task.title = "changed calendar";
        check(JsonUtility.ToJson(source) == original, "Task snapshots, filtered views and calendar views are detached from authoritative data");
        foreach (var zone in new[] { TimeZoneInfo.Utc, TimeZoneInfo.Local, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time") })
        foreach (FocusStatisticsPeriod period in Enum.GetValues(typeof(FocusStatisticsPeriod)))
            check(Fingerprint(service.QueryFocusStatistics(Now.AddDays(-1), period, zone)) == Fingerprint(FocusStatistics.Query(source.focusSessions, source.tags, Now.AddDays(-1), period, zone)), "Narrow Statistics query preserves " + period + " in " + zone.Id);
        var stats = service.QueryFocusStatistics(Now.AddDays(-1), FocusStatisticsPeriod.Day, TimeZoneInfo.Utc);
        stats.tags[0].name = "changed statistics"; stats.tags[0].buckets[0] = 99999;
        var palette = service.FocusStatisticsTagCatalog(); palette[0].colorHex = "changed palette";
        check(JsonUtility.ToJson(source) == original, "Statistics results and palette cannot mutate authoritative sessions/tags");
    }
    static void SaveRegressions(string directory, Action<bool, string> check)
    {
        string path = Path.GetFullPath(Path.Combine(directory, "save-regression.json")); var store = new PlayerDataStore(path);
        check(store.TrySave(new PlayerData { username = "first" }, out _) && store.TrySave(new PlayerData { username = "second" }, out _), "Atomic save establishes primary and backup");
        using (var locked = new FileStream(path + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            check(!store.TrySave(new PlayerData { username = "failed" }, out var failureMessage) && store.Data.username == "second", "Failed write preserves committed memory");
            check(failureMessage == "Could not save your changes. Please try again.", "Save failure offers retry without exposing exception text or private paths");
        }
        check(store.TrySave(new PlayerData { username = "retry" }, out _) && new PlayerDataStore(path).Data.username == "retry", "Retry after write failure commits and reloads");
        string committed = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            for (int attempt = 0; attempt < 3; attempt++)
                check(!store.TrySave(new PlayerData { username = "replacement failed" }, out _) && store.Data.username == "retry"
                    && File.ReadAllText(path) == committed, "Atomic replacement failure preserves committed memory/file on retry " + attempt);
        }
        check(new PlayerDataStore(path).Data.username == "retry", "Reload ignores uncommitted temporary output after replacement failure");
        check(store.TrySave(new PlayerData { username = "after replacement retry" }, out _)
            && new PlayerDataStore(path).Data.username == "after replacement retry", "Retry after replacement failure commits and reloads");
        string backup = File.ReadAllText(path + ".bak"); File.WriteAllText(path, "{broken}");
        check(store.TrySave(new PlayerData { username = "recovered" }, out _) && File.ReadAllText(path + ".bak") == backup, "Corrupt primary never replaces last valid backup");
        string primary = File.ReadAllText(path); DateTime modified = File.GetLastWriteTimeUtc(path);
        string newer = System.Text.RegularExpressions.Regex.Replace(primary, "(\"version\"\\s*:\\s*)5", "${1}9"); check(newer != primary && newer.Length == primary.Length, "Newer-version fixture changes content without changing length");
        File.WriteAllText(path, newer); File.SetLastWriteTimeUtc(path, modified);
        check(!store.TrySave(new PlayerData(), out _) && File.ReadAllText(path) == newer && store.Data.username == "recovered", "Same-size same-timestamp newer external primary blocks saving");
        string second = Path.Combine(directory, "newer-backup.json"); var backupStore = new PlayerDataStore(second);
        check(backupStore.TrySave(new PlayerData(), out _), "Backup-version fixture initializes"); File.WriteAllText(second + ".bak", newer);
        check(!backupStore.TrySave(new PlayerData(), out _), "Newer external backup blocks saving");
        string unicodePath = Path.Combine(directory, "unicode-save.json");
        const string unicodeName = "\u732b\u54aa \ud83d\udc31";
        File.WriteAllText(unicodePath, JsonUtility.ToJson(new PlayerData { version = 5, username = unicodeName }), Encoding.Unicode);
        var unicode = new PlayerDataStore(unicodePath);
        check(unicode.Data.username == unicodeName, "BOM-encoded existing save loads Unicode names");
        check(unicode.TrySave(PlayerDataStore.Copy(unicode.Data), out var unicodeError), "First Unicode save succeeds: " + unicodeError);
        check(unicode.TrySave(PlayerDataStore.Copy(unicode.Data), out unicodeError), "Second cached Unicode save succeeds: " + unicodeError);
        for (int attempt = 0; attempt < 10; attempt++)
            check(unicode.TrySave(PlayerDataStore.Copy(unicode.Data), out unicodeError), "Repeated Unicode save " + attempt + " succeeds: " + unicodeError);
        check(new PlayerDataStore(unicodePath).Data.username == unicodeName, "Unicode names survive cached comparison, atomic saves and reload");
        string comparePath = Path.Combine(directory, "comparison-boundaries.json");
        var comparisonStore = new PlayerDataStore(comparePath);
        check(comparisonStore.TrySave(new PlayerData { username = new string('a', 17003) }, out _), "Byte comparison fixture saves");
        byte[] content = File.ReadAllBytes(comparePath);
        var cache = typeof(PlayerDataStore).GetField("primaryValidation", Hidden).GetValue(comparisonStore);
        var matches = typeof(PlayerDataStore).GetMethod("Matches", BindingFlags.Static | BindingFlags.NonPublic);
        using (var stream = new MemoryStream(content)) check((bool)matches.Invoke(null, new object[] { cache, stream }), "Unchanged complete bytes match the validation cache");
        foreach (int offset in new[] { 0, 1, 7, 8, 15, 8191, 8192, 16383, 16384, content.Length - 1 }) {
            byte[] changed = (byte[])content.Clone(); changed[offset] ^= 1;
            using (var stream = new MemoryStream(changed)) check(!(bool)matches.Invoke(null, new object[] { cache, stream }), "Validation detects a single changed byte at " + offset);
        }
        using (var stream = new MemoryStream(content, 0, content.Length - 1))
            check(!(bool)matches.Invoke(null, new object[] { cache, stream }), "Validation rejects truncated data");
    }
    static void LargeSaveRegressions(string directory, Action<bool, string> check)
    {
        string path = Path.GetFullPath(Path.Combine(directory, "large-save-failures.json"));
        var store = new PlayerDataStore(path);
        var data = Fixture(1000, 1095);
        check(store.TrySave(data, out _) && store.TrySave(PlayerDataStore.Copy(data), out _), "Large failure fixture creates primary and backup");
        check(new FileInfo(path).Length > 8_000_000, "Failure checks exercise the real large-file parallel path");
        var committedData = store.Data;
        var candidate = PlayerDataStore.Copy(committedData); candidate.fishCoins++;
        string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
        foreach (string lockedPath in new[] { path + ".tmp", path }) {
            using (var locked = new FileStream(lockedPath, lockedPath == path ? FileMode.Open : FileMode.OpenOrCreate,
                lockedPath == path ? FileAccess.Read : FileAccess.ReadWrite, lockedPath == path ? FileShare.Read : FileShare.None)) {
                for (int attempt = 0; attempt < 3; attempt++)
                    check(!store.TrySave(candidate, out var error) && !string.IsNullOrEmpty(error) && ReferenceEquals(store.Data, committedData)
                        && File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == backup,
                        "Large write/replacement failure preserves committed data and backup: " + Path.GetFileName(lockedPath) + " attempt " + attempt);
            }
        }
        check(new PlayerDataStore(path).Data.fishCoins == committedData.fishCoins, "Large reload ignores uncommitted temporary data");
        check(store.TrySave(candidate, out _) && new PlayerDataStore(path).Data.fishCoins == candidate.fishCoins,
            "Large retry commits once and survives reload");
        check(File.ReadAllText(path) == JsonUtility.ToJson(candidate), "Parallel save preserves the exact existing compact JSON format");
        File.WriteAllText(path, "{broken}");
        var recovered = new PlayerDataStore(path);
        check(recovered.Data.fishCoins == committedData.fishCoins && !string.IsNullOrEmpty(recovered.LoadNotice), "Large corrupt primary loads the last valid backup with a notice");
        backup = File.ReadAllText(path + ".bak");
        check(recovered.TrySave(PlayerDataStore.Copy(recovered.Data), out _) && File.ReadAllText(path + ".bak") == backup
            && Directory.GetFiles(directory, "large-save-failures.json.corrupt.*").Any(), "Large recovered save keeps the valid backup and archives corrupt primary");
        var modified = File.GetLastWriteTimeUtc(path + ".bak");
        string newer = System.Text.RegularExpressions.Regex.Replace(backup, "(\"version\"\\s*:\\s*)5", "${1}9");
        File.WriteAllText(path + ".bak", newer); File.SetLastWriteTimeUtc(path + ".bak", modified);
        primary = File.ReadAllText(path);
        check(newer.Length == backup.Length && !recovered.TrySave(PlayerDataStore.Copy(recovered.Data), out _)
            && File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == newer,
            "Parallel cached backup detects same-size same-time newer content and blocks writes");
    }
    static void ReplacementRetryRegressions(string directory, Action<bool, string> check)
    {
        var retry = typeof(PlayerDataStore).GetMethod("ReplaceFile", Hidden);
        foreach (string scenario in new[] { "transient", "persistent", "newer-primary", "newer-backup" }) {
            string path = Path.GetFullPath(Path.Combine(directory, "retry-" + scenario + ".json"));
            var store = new PlayerDataStore(path);
            check(store.TrySave(new PlayerData { username = "old" }, out _), "Prepare replacement retry fixture " + scenario);
            if (scenario != "transient") check(store.TrySave(new PlayerData { username = "old" }, out _), "Prepare existing retry backup");
            string primary = File.ReadAllText(path), backup = File.Exists(path + ".bak") ? File.ReadAllText(path + ".bak") : null;
            var candidate = PlayerDataStore.Copy(store.Data); candidate.username = "new";
            string pending = JsonUtility.ToJson(candidate), temporary = path + ".tmp";
            File.WriteAllText(temporary, pending);
            int calls = 0; string external = null;
            Action<string, string, string> writer = (source, destination, copy) => {
                calls++;
                if (scenario == "transient" && calls == 2) { File.Replace(source, destination, copy); return; }
                if (scenario.StartsWith("newer-")) {
                    string changed = scenario == "newer-primary" ? destination : destination + ".bak";
                    var modified = File.GetLastWriteTimeUtc(changed);
                    external = File.ReadAllText(changed).Replace("\"version\":5", "\"version\":9");
                    File.WriteAllText(changed, external); File.SetLastWriteTimeUtc(changed, modified);
                }
                throw new IOException("Injected replacement failure before commit");
            };
            Exception failure = null;
            try { retry.Invoke(store, new object[] { temporary, true, writer }); }
            catch (TargetInvocationException error) { failure = error.InnerException; }
            if (scenario == "transient") check(failure == null && calls == 2 && File.ReadAllText(path) == pending
                && File.ReadAllText(path + ".bak") == primary && !File.Exists(temporary), "Transient retry commits only the prepared file and preserves previous primary as backup");
            else if (scenario == "persistent") check(failure is IOException && calls == 3 && File.ReadAllText(path) == primary
                && File.ReadAllText(path + ".bak") == backup, "Persistent replacement failure stops after three attempts without changing either original");
            else check(failure is IOException && calls == 1
                && File.ReadAllText(path) == (scenario == "newer-primary" ? external : primary)
                && File.ReadAllText(path + ".bak") == (scenario == "newer-backup" ? external : backup),
                "Retry aborts when same-size same-time external content changes: " + scenario);
        }
    }
    static void Write(Report report, string directory)
    {
        File.WriteAllText(Path.Combine(directory, "metrics.json"), JsonUtility.ToJson(report, true));
        var csv = new StringBuilder("operation,activeTasks,days,occurrences,history,focusSessions,warmup,repeats,msPerCall,bytesPerCall,allocationCounterAvailable,managedHeapDeltaAllRepeats,gen0Collections\n");
        foreach (var m in report.metrics) csv.Append(m.operation).Append(',').Append(m.activeTasks).Append(',').Append(m.days).Append(',').Append(m.occurrences).Append(',')
            .Append(m.history).Append(',').Append(m.focusSessions).Append(',').Append(m.warmup).Append(',').Append(m.repeats).Append(',')
            .Append(m.millisecondsPerCall.ToString("F6", CultureInfo.InvariantCulture)).Append(',').Append(m.bytesPerCall).Append(',').Append(m.allocationCounterAvailable).Append(',').Append(m.managedHeapDelta).Append(',').Append(m.gen0Collections).Append('\n');
        File.WriteAllText(Path.Combine(directory, "metrics.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(directory, "measurement-notes.txt"), "Editor main-thread wall time; warmups/repeats and fixture sizes are recorded per operation. A zero/unsupported Mono allocation counter is reported as available=false, bytes=-1, never as zero GC. Managed heap delta and collection counts cover all repeats and include collection/native-bridge effects; they are auxiliary and are NOT allocated bytes. Performance evidence is specific to this Editor run, not standalone player FPS.");
    }
}
