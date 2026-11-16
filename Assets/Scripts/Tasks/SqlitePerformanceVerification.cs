#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Profiling;

// Shared isolated benchmark. Scope is recorded from the actual executing player.
public static partial class SqlitePerformanceVerification
{
    const int Samples = 100, Warmups = 5;
    const double ScalingTolerance = 3;
    [Serializable] sealed class Metric {
        public string engine, operation, status = "running", rawSamples;
        public int activeTasks, historicalRecords, samples, plannedSamples = Samples, warmups = Warmups;
        public long fileBytes, walBytes, commandWindowAllocatedBytes, sqlStatements;
        public double p50, p95, maximum, mainP95;
        public double workerP95, queueWaitP95, unattributedP95;
        [NonSerialized] public List<double> workerSamples = new List<double>(), queueSamples = new List<double>(), unattributedSamples = new List<double>();
    }
    [Serializable] sealed class Scaling {
        public string operation;
        public int activeTasks;
        public double p95At10k, p95At100k, allowedP95, ratio;
        public bool passed;
    }
    [Serializable] sealed class Report {
        public string utc, unity, device, sqlite, status = "running", failure;
        public string scope;
        public string allocationScope = "Unity GC Allocated In Frame cumulative counter across the command window, calibrated with a 4096-byte allocation; includes all threads and unrelated work while awaiting completion, excludes fixture preparation before the window";
        public string workerScope = "Worker time includes command plus projection SQL/serialization; queue wait includes worker scheduling; unattributed remainder is not a pure callback timer";
        public string scalingRule = "For identical active counts and 100 samples, 100k P95 <= 3*max(10k P95,1ms); engineering guard, not proof of zero growth";
        public List<Metric> metrics = new List<Metric>();
        public List<Scaling> scaling = new List<Scaling>();
    }
    sealed class Clock : ITaskClock { public DateTime LocalNow => new DateTime(2026, 11, 16, 12, 0, 0); public DateTime UtcNow => LocalNow.AddHours(-8); }
    // Unity's Mono GC.GetAllocatedBytesForCurrentThread returns zero on this
    // player. GC.Alloc samples count allocations but their Value is not byte
    // metadata. Read the engine's byte counter instead, across frame resets.
    sealed class AllocationProbe : IDisposable
    {
        ProfilerRecorder recorder;
        readonly long initial;
        internal AllocationProbe()
        {
            recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1024,
                ProfilerRecorderOptions.SumAllSamplesInFrame);
            initial = recorder.CurrentValue;
        }
        internal long Stop()
        {
            if (!recorder.Valid || recorder.Count >= 1024) throw new InvalidOperationException("Allocation recorder unavailable or capacity exceeded.");
            long bytes = recorder.CurrentValue - initial;
            for (int i = 0; i < recorder.Count; i++) bytes += recorder.GetSample(i).Value;
            recorder.Stop();
            if (bytes < 0) throw new InvalidOperationException("Allocation counter reset was not captured.");
            return bytes;
        }
        public void Dispose() => recorder.Dispose();
    }
    static async Task CalibrateAllocations(Action<bool, string> check)
    {
        using (var probe = new AllocationProbe()) {
            var buffer = new byte[4096]; buffer[0] = 1;
            long bytes = probe.Stop(); GC.KeepAlive(buffer);
            check(bytes >= 4096, "Unity allocation recorder detects a known 4096-byte allocation");
        }
        using (var probe = new AllocationProbe()) {
            var first = new byte[4096]; first[0] = 1;
            await Task.Delay(40);
            var second = new byte[4096]; second[0] = 1;
            long bytes = probe.Stop(); GC.KeepAlive(first); GC.KeepAlive(second);
            check(bytes >= 8192, "Unity allocation recorder retains bytes across frame boundaries");
        }
    }
    static PlayerData Fixture(int active, int history)
    {
        var data = new PlayerData { username = "SQLite benchmark", tagsInitialized = true, experience = 550, tutorial = new TutorialProgress { version = 1, dismissed = true } };
        data.tags.Add(new QuestTag { id = "tag", name = "Study" });
        for (int i = 0; i < active; i++) data.tasks.Add(new QuestTask { id = "active-" + i, title = "Task " + i, description = "Study and revision" });
        for (int i = 0; i < history; i++) {
            string day = TaskService.DateKey(new DateTime(2020, 1, 1).AddDays(i % 1000));
            if ((i & 1) == 0) data.history.Add(new TaskCompletion { id = "history-" + i, taskId = "old-" + i, day = "once", completedLocalDay = day,
                rewardedLocalDay = day, completedUtc = day + "T04:00:00Z", snapshot = new QuestTask { id = "old-" + i, title = "Historical task " + i } });
            else data.focusSessions.Add(new FocusSession { id = "focus-" + i, status = FocusSessionStatus.Completed, elapsedSeconds = 1500,
                eligibleSeconds = 1500, rewardDay = day, completedUtc = day + "T04:00:00Z" });
        }
        PlayerDataStore.Normalize(data); return data;
    }
    static double P(IEnumerable<double> data, double p) { var a = data.OrderBy(x => x).ToArray(); return a[(int)Math.Ceiling(a.Length * p) - 1]; }
    static void Bound(DateTime deadline) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Performance verification exceeded 15 minutes."); }
    static void Save(string folder, Report report) => File.WriteAllText(Path.Combine(folder, "metrics.json"), JsonUtility.ToJson(report, true));
    static void AddSample(Metric metric, List<double> samples, List<double> main, double elapsed, double mainElapsed, long allocated, long sql, double worker = 0, double queue = 0)
    {
        samples.Add(elapsed); main.Add(mainElapsed); metric.samples = samples.Count;
        metric.commandWindowAllocatedBytes += allocated; metric.sqlStatements += sql;
        metric.p50 = P(samples, .5); metric.p95 = P(samples, .95); metric.maximum = samples.Max(); metric.mainP95 = P(main, .95);
        double unattributed = Math.Max(0, elapsed-mainElapsed-worker-queue);
        metric.workerSamples.Add(worker); metric.queueSamples.Add(queue); metric.unattributedSamples.Add(unattributed);
        metric.workerP95=P(metric.workerSamples,.95); metric.queueWaitP95=P(metric.queueSamples,.95); metric.unattributedP95=P(metric.unattributedSamples,.95);
        File.AppendAllText(metric.rawSamples, string.Join(",", metric.samples.ToString(CultureInfo.InvariantCulture), elapsed.ToString("R", CultureInfo.InvariantCulture),
            mainElapsed.ToString("R", CultureInfo.InvariantCulture), allocated.ToString(CultureInfo.InvariantCulture), sql.ToString(CultureInfo.InvariantCulture),
            worker.ToString("R",CultureInfo.InvariantCulture),queue.ToString("R",CultureInfo.InvariantCulture),unattributed.ToString("R",CultureInfo.InvariantCulture)) + "\n");
    }
    static Metric Begin(Report report, string caseFolder, string engine, string operation, int active, int count)
    {
        var metric = new Metric { engine = engine, operation = operation, activeTasks = active, historicalRecords = count,
            rawSamples = Path.GetFullPath(Path.Combine(caseFolder, engine + "-" + operation + ".csv")) };
        report.metrics.Add(metric); File.WriteAllText(metric.rawSamples, "sample,endToEndMs,mainThreadMs,commandWindowAllocatedBytes,sqlStatements,workerMs,queueWaitMs,unattributedMs\n"); return metric;
    }
    static QuestTask Edit(int sample) => new QuestTask { id = "active-0", title = "Task 0 revision " + sample, description = "Study and revision" };
    public static async Task Run(string folder, Action<bool, string> check)
    {
        if (CompanionProtocol.HasArgument("--sqlite-business-performance")) { await RunBusiness(folder, check); return; }
        Directory.CreateDirectory(folder);
        await CalibrateAllocations(check);
        var report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, device = SystemInfo.processorType,
            scope = Application.isEditor ? "Unity Editor probe; not Windows build or rendered-frame acceptance" : Application.platform + " Development Player; domain command benchmark, not rendered-frame or power-loss acceptance" };
        var deadline = DateTime.UtcNow.AddMinutes(15); Save(folder, report);
        try {
            foreach (int active in new[] { 100, 1000 }) foreach (int count in new[] { 1000, 10000, 100000 }) {
                Bound(deadline);
                string caseFolder = Path.Combine(folder, active + "-" + count); Directory.CreateDirectory(caseFolder);
                string source = Path.Combine(caseFolder, "source.json"), legacyPath = Path.Combine(caseFolder, "legacy.json");
                File.WriteAllText(source, JsonUtility.ToJson(Fixture(active, count))); File.Copy(source, legacyPath);
                var legacy = new TaskService(new PlayerDataStore(legacyPath), new Clock());
                foreach (string operation in new[] { "SaveTag", "EditTask" }) {
                    for (int i = 0; i < Warmups; i++) {
                        Bound(deadline); check(operation == "SaveTag" ? legacy.SaveTag("tag", "Warm " + i, out _) : legacy.Upsert(Edit(-i - 1), null, out _), "JSON " + operation + " warm-up"); await Task.Yield();
                    }
                    var metric = Begin(report, caseFolder, "JSON", operation, active, count); Save(folder, report);
                    var samples = new List<double>(); var main = new List<double>();
                    for (int i = 0; i < Samples; i++) {
                        Bound(deadline); using var allocation = new AllocationProbe(); var timer = Stopwatch.StartNew();
                        bool succeeded = operation == "SaveTag" ? legacy.SaveTag("tag", "Study " + i, out _) : legacy.Upsert(Edit(i), null, out _);
                        double elapsed = timer.Elapsed.TotalMilliseconds; long allocated = allocation.Stop();
                        AddSample(metric, samples, main, elapsed, elapsed, allocated, 0); if (i % 10 == 9) Save(folder, report);
                        check(succeeded, "JSON " + operation + " sample " + i); await Task.Yield();
                    }
                    metric.status = "complete"; metric.fileBytes = new FileInfo(legacyPath).Length; Save(folder, report);
                }
                var repo = new SqlitePlayerRepository(Path.Combine(caseFolder, "save.sqlite3"), source);
                try {
                    var service = new SqlitePlayerService(repo, new Clock());
                    check((await service.InitializeAsync()).Succeeded, "Benchmark migration succeeds"); report.sqlite = service.StorageVersion;
                    var plans = await repo.ExplainForVerificationAsync(); check(plans.Succeeded, "Writer pragmas and query plans are available");
                    File.WriteAllLines(Path.Combine(caseFolder, "query-plans.txt"), plans.Value);
                    check(plans.Value.Contains("writer synchronous=2") && plans.Value.Contains("writer journal_mode=wal") && plans.Value.Contains("writer foreign_keys=1"), "Measured writer retains FULL, WAL and foreign-key checks");
                    foreach (string operation in new[] { "SaveTag", "EditTask" }) {
                        for (int i = 0; i < Warmups; i++) {
                            Bound(deadline);
                            check((operation == "SaveTag" ? await service.SaveTagAsync("tag", "Warm " + i, TagColors.DefaultHex) : await service.UpsertAsync(Edit(-i - 1), null)).Succeeded, "SQLite " + operation + " warm-up");
                        }
                        check((await repo.CountSqlForVerificationAsync()).Succeeded, "SQL counting enabled without recording parameters");
                        var metric = Begin(report, caseFolder, "SQLite", operation, active, count); Save(folder, report);
                        var samples = new List<double>(); var main = new List<double>();
                        for (int i = 0; i < Samples; i++) {
                            Bound(deadline); using var allocation = new AllocationProbe(); long sqlStart = repo.VerificationSqlCount; var timer = Stopwatch.StartNew();
                            var result = operation == "SaveTag" ? await service.SaveTagAsync("tag", "Study " + i, TagColors.DefaultHex) : await service.UpsertAsync(Edit(i), null);
                            double elapsed = timer.Elapsed.TotalMilliseconds; long allocated = allocation.Stop();
                            AddSample(metric, samples, main, elapsed, service.LastCommandMainMilliseconds, allocated, repo.VerificationSqlCount - sqlStart,service.LastCommandStorageMilliseconds,service.LastCommandQueueMilliseconds);
                            if (i % 10 == 9) Save(folder, report); check(result.Succeeded, "SQLite " + operation + " sample " + i);
                        }
                        metric.status = "complete"; metric.fileBytes = new FileInfo(repo.DatabasePath).Length;
                        metric.walBytes = File.Exists(repo.DatabasePath + "-wal") ? new FileInfo(repo.DatabasePath + "-wal").Length : 0; Save(folder, report);
                        check(metric.mainP95 <= 5, "SQLite " + operation + " dispatch/publication P95 <= 5ms (not rendered frame time)");
                        check(metric.p95 <= 200, "SQLite " + operation + " command P95 <= 200ms");
                        if (count == 100000) {
                            var baseline = report.metrics.Single(m => m.engine == "SQLite" && m.operation == operation && m.activeTasks == active && m.historicalRecords == 10000);
                            var scale = new Scaling { operation = operation, activeTasks = active, p95At10k = baseline.p95, p95At100k = metric.p95,
                                allowedP95 = ScalingTolerance * Math.Max(1, baseline.p95), ratio = metric.p95 / Math.Max(.001, baseline.p95) };
                            scale.passed = baseline.samples == Samples && metric.samples == Samples && metric.p95 <= scale.allowedP95;
                            report.scaling.Add(scale); Save(folder, report);
                            check(scale.passed, "SQLite " + operation + " fixed-active 10k->100k P95 stays inside the documented 3x engineering guard");
                            check(metric.sqlStatements <= baseline.sqlStatements + Samples, "SQLite " + operation + " SQL count does not grow with history");
                        }
                    }
                    var page = await service.QueryHistoryPageAsync(); check(page.Succeeded && page.Value.Rows.Count == 100, "History query hydrates one page");
                } finally { await repo.CloseAsync(); }
            }
            report.status = "passed";
        } catch (Exception error) { report.status = "failed"; report.failure = error.GetType().Name + ": " + error.Message; throw; }
        finally { Save(folder, report); }
    }
}
#endif
