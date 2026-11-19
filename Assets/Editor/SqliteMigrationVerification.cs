using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Explicit request only. Never constructs GameDataManager or opens a live save.
[InitializeOnLoad]
public static class SqliteMigrationVerification
{
    const string Request = "Temp/verify-sqlite-migration.request";
    const string PerformanceRequest = "Temp/verify-sqlite-performance.request";
    const string LateLegacyRequest = "Temp/verify-late-legacy.request";
    const string SafetyRequest = "Temp/verify-sqlite-safety.request";
    static Task running;
    static readonly List<string> checks = new List<string>();
    static SqliteMigrationVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (running != null) { EditorApplication.QueuePlayerLoopUpdate(); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(LateLegacyRequest)) { File.Delete(LateLegacyRequest); running = RunSafety(lateLegacyOnly: true); return; }
        if (File.Exists(SafetyRequest)) { File.Delete(SafetyRequest); running = RunSafety(); return; }
        if (File.Exists(PerformanceRequest)) { File.Delete(PerformanceRequest); running = RunPerformance(); return; }
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        running = Run();
    }
    static void Check(bool success, string text) { if (!success) throw new Exception(text); checks.Add(text); }
    static async Task RunSafety(bool lateLegacyOnly = false)
    {
        string mode = lateLegacyOnly ? "late-legacy" : "sqlite-safety";
        string resultPath = "Temp/verify-" + mode + ".result";
        string folder = Path.GetFullPath("Temp/SQLiteMigrationVerification/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + mode);
        Directory.CreateDirectory(folder); checks.Clear();
        File.WriteAllText(resultPath, "RUNNING " + folder);
        try {
            if (lateLegacyOnly) await SqliteMigrationSafetyVerification.VerifyLateLegacy(folder, Check);
            else await SqliteMigrationSafetyVerification.Run(folder, Check);
            File.WriteAllText(resultPath, "PASS " + folder);
        } catch (Exception error) {
            File.WriteAllText(folder + "/result.txt", "FAIL " + error);
            File.WriteAllText(resultPath, "FAIL " + folder);
        } finally { File.WriteAllLines(folder + "/checks.txt", checks); running = null; }
    }
    static async Task RunPerformance()
    {
        string folder = Path.GetFullPath("Temp/SQLiteMigrationVerification/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-performance");
        Directory.CreateDirectory(folder);
        File.WriteAllText("Temp/verify-sqlite-performance.result", "RUNNING " + folder);
        try {
            await SqlitePerformanceVerification.Run(folder, (ok, message) => { if (!ok) throw new Exception(message); });
            File.WriteAllText("Temp/verify-sqlite-performance.result", "PASS " + folder);
        } catch (Exception error) {
            File.WriteAllText(folder + "/result.txt", "FAIL " + error);
            File.WriteAllText("Temp/verify-sqlite-performance.result", "FAIL " + folder);
            Debug.LogException(error);
        } finally { running = null; }
    }
    static T Success<T>(OperationResult<T> result) { Check(result.Succeeded, "Operation: " + (result.Message ?? "success")); return result.Value; }
    static async Task VerifySchemaTwo(string folder)
    {
        string path=Path.Combine(folder,"schema-upgrade.sqlite3"), portable=Path.Combine(folder,"schema-one.catquestbackup");
        var repo=new SqlitePlayerRepository(path);
        try {
            Success(await repo.InitializeAsync());
            Success(await repo.ExecuteAsync(tx=>{
                for(int i=0;i<205;i++)tx.Put(PlayerRecordKind.History,new TaskCompletion {
                    id="schema-history-"+i,taskId="deleted-"+i,day="once",completedUtc="2026-11-15T12:00:00Z",
                    completedLocalDay="2026-11-15",rewardedLocalDay="2026-11-15",
                    snapshot=new QuestTask{id="deleted-"+i,title="研究 ÄLPHA "+i,difficulty=TaskDifficulty.Easy,tagIds=new List<string>{"deleted-tag"}},
                    tagNames=new List<string>{"旧标签"}});
                return true;
            }));
            string expected=JsonUtility.ToJson(Success(await repo.ReadAllForVerificationAsync()));
            Success(await repo.ExportBackupAsync(portable)); await repo.CloseAsync();
            foreach(string source in new[]{path,portable})using(var db=new SQLite.SQLiteConnection(source)) {
                db.Execute("DROP TABLE cq_history_search");db.Execute("DROP TABLE cq_history_tags");db.Execute("DROP TABLE cq_mail_state");db.Execute("PRAGMA user_version=1");
            }
            repo=new SqlitePlayerRepository(path);
            repo.FaultPoint=stage=>{if(stage=="schema-upgrade-rows")throw new IOException("Injected schema upgrade interruption");};
            Check(!(await repo.InitializeAsync()).Succeeded,"Schema upgrade interruption fails closed");await repo.CloseAsync();
            using(var db=new SQLite.SQLiteConnection(path))Check(db.ExecuteScalar<int>("PRAGMA user_version")==1&&db.GetTableInfo("cq_history_search").Count==0,"Schema DDL rows and version roll back together");
            Check(Directory.GetFiles(folder,"schema-upgrade.sqlite3.before-schema-1-to-3-*.sqlite3").Length>0,"Pre-upgrade snapshot retained");
            foreach(string backup in Directory.GetFiles(folder,"schema-upgrade.sqlite3.before-schema-1-to-3-*.sqlite3"))using(var db=new SQLite.SQLiteConnection(backup))
                Check(db.ExecuteScalar<string>("PRAGMA integrity_check")=="ok"&&db.ExecuteScalar<int>("PRAGMA user_version")==1&&db.ExecuteScalar<int>("SELECT COUNT(*) FROM cq_History")==205,"Pre-upgrade backup is a complete schema-one snapshot");
            repo=new SqlitePlayerRepository(path);Success(await repo.InitializeAsync());
            Check(JsonUtility.ToJson(Success(await repo.ReadAllForVerificationAsync()))==expected,"Schema retry preserves every field");
            var page=Success(await repo.QueryHistoryPageAsync(null,"älpha",TaskDifficulty.Easy,new[]{"deleted-tag"}));
            Check(page.Rows.Count==100&&page.NextCursor!=null&&repo.HistoryPayloadsReadForVerification==100,"Unicode filtered history hydrates only 100 deleted-task snapshots");
            var next=Success(await repo.QueryHistoryPageAsync(page.NextCursor,"ÄLPHA",TaskDifficulty.Easy,new[]{"deleted-tag"}));
            Check(next.Rows.Count==100&&!page.Rows.Select(r=>r.history.id).Intersect(next.Rows.Select(r=>r.history.id)).Any(),"History cursor preserves stable order without duplicates");
            byte[] sourceBytes=File.ReadAllBytes(portable);
            repo.FaultPoint=stage=>{if(stage=="import-before-commit")throw new IOException("Injected schema-one import failure");};
            Check(!(await repo.ImportBackupAsync(portable)).Succeeded,"Schema-one import fault fails without commit");repo.FaultPoint=null;
            Check(File.ReadAllBytes(portable).SequenceEqual(sourceBytes)&&JsonUtility.ToJson(Success(await repo.ReadAllForVerificationAsync()))==expected,"Failed schema-one import preserves source and target");
            var recoveryTarget=new SqlitePlayerRepository(Path.Combine(folder,"schema-one-recovery.sqlite3"));
            try {
                recoveryTarget.FaultPoint=stage=>{if(stage=="recovery-before-publish")throw new IOException("Injected restore failure");};
                Check(!(await recoveryTarget.RecoverFromSnapshotAsync(portable)).Succeeded&&File.ReadAllBytes(portable).SequenceEqual(sourceBytes),"Failed schema-one restore preserves source bytes");
            } finally {await recoveryTarget.CloseAsync();}
            Success(await repo.ImportBackupAsync(portable));
            Check(Success(await repo.QueryHistoryPageAsync(null,"旧标签",null,new[]{"deleted-tag"})).Rows.Count==100,"Schema-one portable backup builds current history indexes");
            using(var db=new SQLite.SQLiteConnection(portable))Check(db.ExecuteScalar<int>("PRAGMA user_version")==1,"Old portable source unchanged");
            Success(await repo.ExecuteAsync(tx=>{
                var receipt=tx.Read<TaskCompletion>(PlayerRecordKind.History,"schema-history-0");receipt.revoked=true;tx.Put(PlayerRecordKind.History,receipt);
                tx.Delete(PlayerRecordKind.History,"schema-history-1");return true;
            }));
            var remaining=Success(await repo.QueryHistoryPageAsync(null,"",TaskDifficulty.Easy,new[]{"deleted-tag"}));
            Check(remaining.TotalCount==203&&!remaining.Rows.Any(r=>r.history.id=="schema-history-0"||r.history.id=="schema-history-1"),"History revoke and deletion keep compact indexes consistent");
            Success(await repo.ExecuteAsync(tx=>{
                tx.Delete(PlayerRecordKind.History,"schema-history-1");tx.Delete(PlayerRecordKind.History,"schema-history-0");
                tx.Put(PlayerRecordKind.FocusSessions,new FocusSession{id="delete-focus",status=FocusSessionStatus.Completed,eligibleSeconds=125});
                tx.Put(PlayerRecordKind.FocusSessions,new FocusSession{id="delete-focus-early",status=FocusSessionStatus.EndedEarly,eligibleSeconds=60});
                tx.Put(PlayerRecordKind.FocusSessions,new FocusSession{id="delete-focus-cancelled",status=FocusSessionStatus.Cancelled,eligibleSeconds=70});
                return true;
            }));
            var focusBefore=Success(await repo.QueryAccountAggregateAsync("test","test"));
            Check(focusBefore.totalFocusMinutes==3,"Focus delete fixture contributes committed duration");
            Success(await repo.ExecuteAsync(tx=>{
                tx.Delete(PlayerRecordKind.FocusSessions,"delete-focus");tx.Delete(PlayerRecordKind.FocusSessions,"delete-focus");
                tx.Delete(PlayerRecordKind.FocusSessions,"delete-focus-early");tx.Delete(PlayerRecordKind.FocusSessions,"delete-focus-cancelled");return true;
            }));
            Check(Success(await repo.QueryAccountAggregateAsync("test","test")).totalFocusMinutes==0
                &&Success(await repo.QueryHistoryPageAsync(null,"",null,null)).TotalCount==203,"Repeated and inactive receipt deletion cannot subtract totals twice");
            Success(await repo.CreateRecoveryBackupAsync(Path.Combine(folder,"after-record-deletions.sqlite3")));
            string checkedBackup=Path.Combine(folder,"after-record-deletions.sqlite3");
            using(var db=new SQLite.SQLiteConnection(checkedBackup))db.Execute("UPDATE cq_history_search SET searchText='tampered' WHERE id='schema-history-2'");
            Check((await repo.ImportBackupAsync(checkedBackup)).Error==PlayerStorageError.Corrupt,"Batched validation rejects compact search metadata tampering");
            Success(await repo.CreateRecoveryBackupAsync(checkedBackup));
            using(var db=new SQLite.SQLiteConnection(checkedBackup))db.Execute("DELETE FROM cq_history_tags WHERE historyId='schema-history-2'");
            Check((await repo.ImportBackupAsync(checkedBackup)).Error==PlayerStorageError.Corrupt,"Batched validation rejects missing snapshot tag associations");
            await repo.CloseAsync();using(var db=new SQLite.SQLiteConnection(path))db.Execute("PRAGMA user_version=999");
            repo=new SqlitePlayerRepository(path);Check((await repo.InitializeAsync()).Error==PlayerStorageError.NewerVersion,"Future schema protected");
        } finally { await repo.CloseAsync(); }
    }
    static PlayerData Fixture()
    {
        var data = new PlayerData { username = "研究猫 🐈", experience = 550, fishCoins = 123, tickets = 8, tagsInitialized = true,
            tutorial = new TutorialProgress { version = 1, dismissed = true },
            account = new AccountPreferences { boundUid = "PRIVATE-OWNER-123", uploadEnabled = true, pendingSnapshotJson = "PRIVATE-PENDING-456", playerName = "研究猫" } };
        data.tasks.Add(new QuestTask { id = "todo", title = "研究 SQL", description = "quotes ' and % and _", daily = false });
        data.tags.Add(new QuestTag { id = "study", name = "Study" });
        data.history.Add(new TaskCompletion { id = "receipt", taskId = "deleted-task", day = "once", completedLocalDay = "2026-11-15", rewardedLocalDay = "2026-11-15", completedUtc = "2026-11-15T04:00:00Z",
            snapshot = new QuestTask { id = "deleted-task", title = "Preserved deleted task" }, experience = 15, fishCoins = 6, revoked = true });
        data.focusSessions.Add(new FocusSession { id = "focus", status = FocusSessionStatus.EndedEarly, rewardDay = "2026-11-15", elapsedSeconds = 90, eligibleSeconds = 90 });
        PlayerDataStore.Normalize(data); return data;
    }
    static async Task Run()
    {
        string folder = Path.GetFullPath("Temp/SQLiteMigrationVerification/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-repository");
        Directory.CreateDirectory(folder); checks.Clear();
        File.WriteAllText("Temp/verify-sqlite-migration.result", "RUNNING " + folder);
        var owned = new List<SqlitePlayerRepository>();
        SqlitePlayerRepository Repo(string name, string legacy = null) { var r = new SqlitePlayerRepository(Path.Combine(folder, name + ".sqlite3"), legacy); owned.Add(r); return r; }
        try {
            string source = Path.Combine(folder, "old.json");
            var data = Fixture(); string json = JsonUtility.ToJson(data); File.WriteAllText(source, json);
            var repo = Repo("main", source);
            string version = Success(await repo.InitializeAsync());
            Check(JsonUtility.ToJson(Success(await repo.ReadAllForVerificationAsync())) == json, "Migration preserves all fields, record order, Unicode, historical amounts and revoked snapshots");
            Check(File.ReadAllText(source) == json, "Original JSON unchanged");
            var competitor = new SqlitePlayerRepository(repo.DatabasePath); owned.Add(competitor);
            Check((await competitor.InitializeAsync()).Error == PlayerStorageError.Busy, "Second owner rejected");
            var changed = await repo.ExecuteAsync(tx => { var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile"); profile.fishCoins += 5; tx.Put(PlayerRecordKind.Profile, profile); return profile.fishCoins; });
            Check(Success(changed) == 128 && changed.Changed.Contains(PlayerRecordKind.Profile), "Small transaction changes only the player record");
            repo.FaultPoint = stage => { if (stage == "before-commit") throw new IOException("Injected failure"); };
            var failed = await repo.ExecuteAsync(tx => { var p = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile"); p.fishCoins = 0; tx.Put(PlayerRecordKind.Profile, p); tx.Delete(PlayerRecordKind.Tasks, "todo"); return true; });
            Check(!failed.Succeeded, "Injected transaction failure propagates"); repo.FaultPoint = null;
            var after = Success(await repo.ReadAllForVerificationAsync());
            Check(after.fishCoins == 128 && after.tasks.Count == 1, "Rollback restores all transaction changes");
            foreach (var fault in new[] {
                (SQLite.SQLite3.Result.Full, PlayerStorageError.DiskFull),
                (SQLite.SQLite3.Result.ReadOnly, PlayerStorageError.AccessDenied),
                (SQLite.SQLite3.Result.Busy, PlayerStorageError.Busy),
                (SQLite.SQLite3.Result.Corrupt, PlayerStorageError.Corrupt) }) {
                repo.FaultPoint = stage => { if (stage == "before-commit") throw SQLite.SQLiteException.New(fault.Item1, "Injected storage status"); };
                var rejected = await repo.ExecuteAsync(tx => { var p = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile"); p.fishCoins = 0; tx.Put(PlayerRecordKind.Profile, p); return true; });
                repo.FaultPoint = null;
                Check(!rejected.Succeeded && rejected.Error == fault.Item2 && Success(await repo.ReadAllForVerificationAsync()).fishCoins == 128,
                    "Injected " + fault.Item1 + " preserves committed state and reports " + fault.Item2);
            }
            int executions = 0;
            repo.FaultPoint = stage => { if (stage == "after-commit") throw new IOException("Acknowledgement interrupted"); };
            var uncertain = await repo.ExecuteAsync(tx => { executions++; var p = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile"); p.username = "Confirmed commit"; tx.Put(PlayerRecordKind.Profile, p); return p.username; });
            repo.FaultPoint = null;
            Check(uncertain.Succeeded && executions == 1 && Success(await repo.ReadAllForVerificationAsync()).username == "Confirmed commit", "Lost commit acknowledgement is resolved from transaction marker without replay");
            var writes = Enumerable.Range(0, 20).Select(_ => repo.ExecuteAsync(tx => { var p = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile"); p.fishCoins++; tx.Put(PlayerRecordKind.Profile, p); return p.fishCoins; })).ToArray();
            await Task.WhenAll(writes);
            Check(writes.All(t => t.Result.Succeeded) && Success(await repo.ReadAllForVerificationAsync()).fishCoins == 148, "Concurrent callers are serialized without lost updates");
            string portable = Path.Combine(folder, "transfer.catquestbackup"); Success(await repo.ExportBackupAsync(portable));
            string bytes = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(portable));
            Check(!bytes.Contains("PRIVATE-OWNER-123") && !bytes.Contains("PRIVATE-PENDING-456"), "Portable backup pages contain no excluded account secrets");
            var other = Repo("other-device"); Success(await other.InitializeAsync()); Success(await other.ImportBackupAsync(portable));
            var imported = Success(await other.ReadAllForVerificationAsync());
            Check(imported.fishCoins == 148 && imported.history.Single().revoked && string.IsNullOrEmpty(imported.account.boundUid) && !imported.account.uploadEnabled, "Device transfer preserves progress without account binding or consent");
            other.FaultPoint = stage => { if (stage == "import-before-commit") throw new IOException("Injected import failure"); };
            Check(!(await other.ImportBackupAsync(portable)).Succeeded, "Import fault propagates"); other.FaultPoint = null;
            Check(JsonUtility.ToJson(Success(await other.ReadAllForVerificationAsync())) == JsonUtility.ToJson(imported), "Failed import keeps target data");
            string recovery = Path.Combine(folder, "recovery.sqlite3"); Success(await repo.CreateRecoveryBackupAsync(recovery));
            using (var check = new SQLite.SQLiteConnection(recovery, SQLite.SQLiteOpenFlags.ReadOnly)) Check(JsonUtility.FromJson<PlayerData>(check.ExecuteScalar<string>("SELECT payload FROM cq_Profile")).fishCoins == 148, "Recovery snapshot includes committed WAL data");
            await repo.CloseAsync();
            File.WriteAllText(source, "broken old JSON");
            var reopened = new SqlitePlayerRepository(repo.DatabasePath, source); owned.Add(reopened); Success(await reopened.InitializeAsync());
            Check(Success(await reopened.ReadAllForVerificationAsync()).fishCoins == 148, "Restart uses SQLite despite changed legacy JSON");
            await reopened.CloseAsync(); File.Move(repo.DatabasePath, repo.DatabasePath + ".removed-for-test");
            var missing = new SqlitePlayerRepository(repo.DatabasePath, source); owned.Add(missing);
            Check((await missing.InitializeAsync()).Error == PlayerStorageError.Corrupt, "Missing migrated database never silently reimports old JSON");
            foreach (string stage in new[] { "migration-journal", "migration-rows", "migration-ready", "migration-published" }) {
                string legacy = Path.Combine(folder, stage + ".json"); File.WriteAllText(legacy, json);
                var interrupted = Repo(stage, legacy); interrupted.FaultPoint = p => { if (p == stage) throw new IOException("Interrupted"); };
                Check(!(await interrupted.InitializeAsync()).Succeeded, "Migration interrupted at " + stage); await interrupted.CloseAsync();
                var retry = new SqlitePlayerRepository(interrupted.DatabasePath, legacy); owned.Add(retry); Success(await retry.InitializeAsync());
                Check(JsonUtility.ToJson(Success(await retry.ReadAllForVerificationAsync())) == json, "Exact migration retry after " + stage);
            }
            string fallback = Path.Combine(folder, "fallback.json"); File.WriteAllText(fallback, "corrupt"); File.WriteAllText(fallback + ".bak", json);
            var fallbackRepo = Repo("fallback", fallback); Success(await fallbackRepo.InitializeAsync()); Check(Success(await fallbackRepo.ReadAllForVerificationAsync()).fishCoins == 123, "Valid JSON backup recovers corrupt primary");
            string future = Path.Combine(folder, "future.json"); File.WriteAllText(future, json); File.WriteAllText(future + ".bak", "{\"version\":999}");
            Check((await Repo("future", future).InitializeAsync()).Error == PlayerStorageError.NewerVersion, "Newer backup blocks migration of older primary");
            string invalid = Path.Combine(folder, "invalid.json"); File.WriteAllText(invalid, "corrupt");
            Check(!(await Repo("invalid", invalid).InitializeAsync()).Succeeded, "Corrupt old data does not become a new empty save");
            var memory = new SqlitePlayerRepository(":memory:"); owned.Add(memory); Success(await memory.InitializeAsync());
            Check(Success(await memory.ReadAllForVerificationAsync()).tutorial != null, "Session-only database initializes correctly");
            await VerifySchemaTwo(folder);
            await SqliteTaskCommandVerification.RunAsync(Path.Combine(folder, "task-commands"), Check);
            await SqliteProgressionVerification.Run(folder, Check);
            await SqliteFocusStatisticsVerification.Run(folder, Check);
            await SqliteMigrationSafetyVerification.Run(folder, Check);
            File.WriteAllText(folder + "/checks.txt", string.Join("\n", checks));
            File.WriteAllText(folder + "/result.txt", DateTime.UtcNow.ToString("O") + " PASS repository verification | Unity " + Application.unityVersion + " | SQLite " + version);
            File.WriteAllText("Temp/verify-sqlite-migration.result", "PASS " + folder);
        } catch (Exception error) {
            File.WriteAllText(folder + "/checks.txt", string.Join("\n", checks)); File.WriteAllText(folder + "/result.txt", "FAIL " + error);
            File.WriteAllText("Temp/verify-sqlite-migration.result", "FAIL " + folder); Debug.LogException(error);
        } finally { foreach (var repo in owned) await repo.CloseAsync(); running = null; }
    }
}
