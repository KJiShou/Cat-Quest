using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public static class SqliteMigrationSafetyVerification
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        string folder = Path.GetFullPath(Path.Combine(root, "migration-safety"));
        if (Directory.Exists(folder)) throw new InvalidOperationException("Safety verification requires a fresh directory.");
        Directory.CreateDirectory(folder);
        var owned = new List<SqlitePlayerRepository>();
        SqlitePlayerRepository Repo(string name, string source = null) { var repo = new SqlitePlayerRepository(Path.Combine(folder, name + ".sqlite3"), source); owned.Add(repo); return repo; }
        var data = new PlayerData { username = "Migration safety", tagsInitialized = true, fishCoins = 123 };
        data.tasks.Add(new QuestTask { id = "task", title = "Keep current progress" });
        PlayerDataStore.Normalize(data);
        string valid = JsonUtility.ToJson(data);
        try {
            string duplicate = Path.Combine(folder, "duplicate.json");
            data.tasks.Add(data.tasks[0].DetachedCopy());
            string invalid = JsonUtility.ToJson(data);
            File.WriteAllText(duplicate, invalid); File.WriteAllText(duplicate + ".bak", valid);
            var conflicting = Repo("duplicate", duplicate);
            var duplicateResult = await conflicting.InitializeAsync();
            check(!duplicateResult.Succeeded && duplicateResult.Error == PlayerStorageError.InvalidData && !File.Exists(conflicting.DatabasePath), "Readable duplicate primary stops instead of silently using older backup");
            check(File.ReadAllText(duplicate) == invalid, "Rejected semantic migration preserves original JSON");

            string source = Path.Combine(folder, "tamper.json");
            File.WriteAllText(source, valid); File.WriteAllText(source + ".bak", valid);
            var interrupted = Repo("tamper", source);
            interrupted.FaultPoint = stage => { if (stage == "migration-journal") throw new IOException("Injected stop"); };
            check(!(await interrupted.InitializeAsync()).Succeeded, "Frozen-source tamper fixture interrupts after journal");
            await interrupted.CloseAsync();
            File.WriteAllText(interrupted.DatabasePath + ".pre-sqlite.json.bak", "changed backup");
            var retry = new SqlitePlayerRepository(interrupted.DatabasePath, source); owned.Add(retry);
            check((await retry.InitializeAsync()).Error == PlayerStorageError.Migration && !File.Exists(retry.DatabasePath), "Changed frozen backup is detected before retry import");

            string liveSource = Path.Combine(folder, "live.json"); File.WriteAllText(liveSource, valid);
            var live = Repo("live", liveSource); var liveInitialized = await live.InitializeAsync();
            check(liveInitialized.Succeeded, "Path-protection fixture initializes: " + liveInitialized.Error + " " + liveInitialized.Message);
            check((await live.ReadAllForVerificationAsync()).Value.tutorial == null, "Legacy null tutorial remains absent after migration");
            string before = JsonUtility.ToJson((await live.ReadAllForVerificationAsync()).Value);
            check(!(await live.CreateRecoveryBackupAsync(live.DatabasePath)).Succeeded, "Recovery backup cannot overwrite active database");
            check(!(await live.CreateRecoveryBackupAsync(live.DatabasePath + "-wal")).Succeeded, "Recovery backup cannot overwrite active WAL");
            check(!(await live.CreateRecoveryBackupAsync(liveSource)).Succeeded, "Recovery backup cannot overwrite legacy source");
            check(JsonUtility.ToJson((await live.ReadAllForVerificationAsync()).Value) == before && File.ReadAllText(liveSource) == valid, "Rejected backup destinations preserve database and legacy data");
            string portable = Path.Combine(folder, "bad-receipt.catquestbackup");
            var receipt = new TaskCompletion { id = "bad", taskId = "task", day = "once", snapshot = new QuestTask { id = "task", title = "Invalid paid amount" }, experience = -1 };
            var invalidReceipt = Repo("negative-receipt");
            check((await invalidReceipt.InitializeAsync()).Succeeded, "Isolated negative receipt fixture initialized");
            // Low-level transaction deliberately bypasses domain validation while
            // maintaining the current schema's derived indexes and summaries.
            check((await invalidReceipt.ExecuteAsync(tx => { tx.Put(PlayerRecordKind.History, receipt); return true; })).Succeeded,
                "Negative receipt fixture retains structurally valid indexes");
            check((await invalidReceipt.ExportBackupAsync(portable)).Succeeded, "Portable validation fixture exported");
            await invalidReceipt.CloseAsync();
            var rejected = await live.ImportBackupAsync(portable);
            check(!rejected.Succeeded && rejected.Error == PlayerStorageError.InvalidData, "Negative reward receipt in valid SQLite file is rejected");
            check(JsonUtility.ToJson((await live.ReadAllForVerificationAsync()).Value) == before, "Invalid portable import leaves target unchanged");
            string snapshot = Path.Combine(folder,"verified-recovery.sqlite3");
            check((await live.CreateRecoveryBackupAsync(snapshot)).Succeeded, "Consistent recovery snapshot created");
            check(!File.Exists(snapshot + "-wal") && !File.Exists(snapshot + "-shm"), "Recovery snapshot is a standalone file");
            var damaged = Repo("damaged"); File.WriteAllText(damaged.DatabasePath,"damaged database evidence");
            check(!(await damaged.InitializeAsync()).Succeeded, "Corrupt save stays closed before explicit recovery");
            damaged.FaultPoint = stage => { if (stage == "recovery-after-publish") throw new IOException("Injected recovery interruption"); };
            check(!(await damaged.RecoverFromSnapshotAsync(snapshot)).Succeeded, "Explicit recovery interruption is reported");
            check(!(await damaged.InitializeAsync()).Succeeded && File.Exists(damaged.DatabasePath + ".recovery-pending.json"), "Interrupted recovery cannot open partially published state");
            damaged.FaultPoint = null;
            check((await damaged.RecoverFromSnapshotAsync(snapshot)).Succeeded && (await damaged.InitializeAsync()).Succeeded, "Explicit retry completes interrupted recovery");
            check(JsonUtility.ToJson((await damaged.ReadAllForVerificationAsync()).Value) == before, "Recovered snapshot preserves every player field");
            check(Directory.GetFiles(folder,"damaged.sqlite3.before-recovery-*").Length > 0, "Recovery retains damaged source evidence");
            var snapshotReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var releaseSnapshot = new System.Threading.ManualResetEventSlim(false)) {
                live.FaultPoint = stage => { if (stage == "maintenance-snapshot") { snapshotReached.TrySetResult(true); if (!releaseSnapshot.Wait(5000)) throw new TimeoutException("Maintenance fixture timed out"); } };
                typeof(SqlitePlayerRepository).GetField("lastRecoveryUtc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(live, DateTime.MinValue);
                var maintenance = live.MaintainAsync();
                try {
                    check(await Task.WhenAny(snapshotReached.Task, Task.Delay(5000)) == snapshotReached.Task, "Maintenance pins its independent read snapshot");
                    var write = live.ExecuteAsync(tx => { var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile,"profile"); profile.fishCoins++; tx.Put(PlayerRecordKind.Profile,profile); return true; });
                    check(await Task.WhenAny(write, Task.Delay(3000)) == write && (await write).Succeeded, "Foreground command commits while maintenance is deliberately paused");
                } finally { releaseSnapshot.Set(); }
                var copied = await maintenance;
                check(copied.Succeeded && copied.Value, "Independent maintenance snapshot completes");
                string maintained = Directory.GetFiles(folder,"live.sqlite3.recovery-*.sqlite3").Single();
                using (var db = new SQLite.SQLiteConnection(maintained,SQLite.SQLiteOpenFlags.ReadOnly)) {
                    var saved = JsonUtility.FromJson<PlayerData>(db.ExecuteScalar<string>("SELECT payload FROM cq_Profile WHERE id='profile'"));
                    check(saved.fishCoins == 123 && long.Parse(db.ExecuteScalar<string>("SELECT value FROM cq_meta WHERE key='revision'")) == copied.Version,
                        "Snapshot content and revision refer to the pinned generation, not concurrent writes");
                }
            }
            live.FaultPoint = null;
            string changingSource = Path.Combine(folder,"changing-source.catquestbackup");
            check((await live.ExportBackupAsync(changingSource)).Succeeded, "Concurrent source fixture exported");
            int exportedCoins;
            using (var writer = new SQLite.SQLiteConnection(changingSource)) {
                writer.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                exportedCoins = JsonUtility.FromJson<PlayerData>(writer.ExecuteScalar<string>("SELECT payload FROM cq_Profile WHERE id='profile'")).fishCoins;
            }
            var raceTarget = Repo("source-race-target"); check((await raceTarget.InitializeAsync()).Succeeded,"Concurrent source target initializes");
            raceTarget.FaultPoint = stage => {
                if (stage != "import-source-validated") return;
                using var writer = new SQLite.SQLiteConnection(changingSource);
                var altered = JsonUtility.FromJson<PlayerData>(writer.ExecuteScalar<string>("SELECT payload FROM cq_Profile WHERE id='profile'"));
                altered.fishCoins += 1000;
                writer.Execute("UPDATE cq_Profile SET payload=? WHERE id='profile'",JsonUtility.ToJson(altered));
            };
            check((await raceTarget.ImportBackupAsync(changingSource)).Succeeded,"Import completes from a pinned source while another connection changes it");
            check((await raceTarget.ReadAllForVerificationAsync()).Value.fishCoins == exportedCoins,"Import uses the same generation it validated");
            raceTarget.FaultPoint = null;
            using (var cleanup = new SQLite.SQLiteConnection(changingSource)) { cleanup.Execute("PRAGMA wal_checkpoint(TRUNCATE)"); cleanup.ExecuteScalar<string>("PRAGMA journal_mode=DELETE"); }
            string missingSettings = Path.Combine(folder,"missing-settings.catquestbackup");
            check((await live.ExportBackupAsync(missingSettings)).Succeeded,"Missing-settings fixture exported");
            using (var damagedSettings = new SQLite.SQLiteConnection(missingSettings)) damagedSettings.Execute("DELETE FROM cq_Settings");
            string targetBeforeMissing = JsonUtility.ToJson((await raceTarget.ReadAllForVerificationAsync()).Value);
            check((await raceTarget.ImportBackupAsync(missingSettings)).Error == PlayerStorageError.Corrupt,"Portable backup missing a required settings row is rejected");
            check(JsonUtility.ToJson((await raceTarget.ReadAllForVerificationAsync()).Value) == targetBeforeMissing,"Missing-settings rejection preserves target progress");
            long beforeImportVersion = (await raceTarget.ReadAllForVerificationAsync()).Version;
            int afterImportFaults = 0;
            raceTarget.FaultPoint = stage => { if (stage == "import-after-commit") { afterImportFaults++; throw new IOException("Injected lost import acknowledgement"); } };
            var committedImport = await raceTarget.ImportBackupAsync(changingSource);
            check(committedImport.Succeeded && committedImport.Version == beforeImportVersion + 1 && afterImportFaults == 1,
                "Lost import acknowledgement is resolved by its committed marker without repeating replacement");
            check((await raceTarget.ReadAllForVerificationAsync()).Value.fishCoins == exportedCoins + 1000,
                "Acknowledgement failure still exposes the committed imported progress");
            raceTarget.FaultPoint = null;
            await raceTarget.CloseAsync();
            var reopenedImport = new SqlitePlayerRepository(raceTarget.DatabasePath); owned.Add(reopenedImport);
            check((await reopenedImport.InitializeAsync()).Succeeded && (await reopenedImport.ReadAllForVerificationAsync()).Value.fishCoins == exportedCoins + 1000,
                "Import with lost acknowledgement survives close and reopen");
            var projectionRepo = Repo("projection-failure");
            var projectionService = new SqlitePlayerService(projectionRepo,new SystemTaskClock());
            check((await projectionService.InitializeAsync()).Succeeded,"Projection failure fixture initializes");
            var rewardTask = await projectionService.UpsertAsync(new QuestTask { title = "Committed reward fixture" },null);
            check(rewardTask.Succeeded,"Projection failure fixture task created");
            bool committed = false; string warning = null; int publications = 0;
            projectionService.StorageWarning += message => warning = message;
            projectionService.Changed += () => publications++;
            projectionRepo.FaultPoint = stage => {
                if (stage == "after-commit") committed = true;
                if (stage == "read-started" && committed) throw new IOException("Injected projection failure");
            };
            var rewarded = await projectionService.CompleteAsync(rewardTask.Value,true);
            check(rewarded.Succeeded && rewarded.Value.experience > 0 && !projectionService.IsReady && publications == 0,
                "Committed reward remains successful while failed projection freezes writes and suppresses stale display publication");
            check(!string.IsNullOrEmpty(warning) && warning.Contains("Restart Cat Quest") && warning == rewarded.Message && warning == projectionService.LastError,
                "Projection failure publishes an actionable storage warning with the committed result");
            check(!(await projectionService.CompleteAsync(rewardTask.Value,true)).Succeeded,"Frozen service rejects repeat input after committed projection failure");
            var savedReward = (await projectionRepo.ReadAllForVerificationAsync()).Value;
            check(savedReward.history.Count == 1 && savedReward.experience == rewarded.Value.experience,"Reward is durably recorded once despite display failure");
            check((await projectionService.PrepareShutdownAsync()).Succeeded,"Projection failure still allows shutdown queue drain");
            await projectionService.CloseForShutdownAsync();
            var rewardReload = new SqlitePlayerRepository(projectionRepo.DatabasePath); owned.Add(rewardReload);
            check((await rewardReload.InitializeAsync()).Succeeded,"Committed reward database reopens after failed projection");
            var reloadedReward = (await rewardReload.ReadAllForVerificationAsync()).Value;
            check(reloadedReward.history.Count == 1 && reloadedReward.experience == savedReward.experience && reloadedReward.fishCoins == savedReward.fishCoins,
                "Restart preserves the single committed reward and balances");
            await VerifyLegacyFocusPrecision(folder, check);
            await VerifyDerivedIndexes(folder,check);
        } finally { foreach (var repo in owned) await repo.CloseAsync(); }
    }
    internal static async Task VerifyLateLegacy(string folder, Action<bool, string> check)
    {
        string source = Path.Combine(folder, "late-legacy.json");
        string database = Path.ChangeExtension(source, ".sqlite3");
        var service = new SqlitePlayerService(new SqlitePlayerRepository(database, source), new SystemTaskClock());
        try {
            check((await service.InitializeAsync()).Succeeded && string.IsNullOrEmpty(service.PlayerName)
                && service.TagCatalog.Count == TaskService.DefaultTags.Length, "Missing JSON initially creates a blank profile with startup tags");
        } finally { await service.CloseAsync(); }

        var legacy = new PlayerData { experience = 550, fishCoins = 123, account = new AccountPreferences { playerName = "Returning player" } };
        legacy.tasks.Add(new QuestTask { id = "legacy-task", title = "Keep this task" });
        string original = JsonUtility.ToJson(legacy);
        File.WriteAllText(source, original);

        var interrupted = new SqlitePlayerRepository(database, source);
        try {
            interrupted.FaultPoint = phase => {
                if (phase == "late-legacy-before-commit") throw new IOException("Injected late migration rollback");
            };
            check(!(await interrupted.InitializeAsync()).Succeeded, "Late JSON migration interruption reports failure");
        } finally { await interrupted.CloseAsync(); }

        string snapshot = Directory.GetFiles(folder, "late-legacy.sqlite3.before-late-legacy-*.sqlite3").Single();
        using (var backup = new SQLite.SQLiteConnection(snapshot, SQLite.SQLiteOpenFlags.ReadOnly))
            check(backup.ExecuteScalar<string>("PRAGMA integrity_check") == "ok"
                && backup.ExecuteScalar<int>("SELECT COUNT(*) FROM cq_Tags") == TaskService.DefaultTags.Length
                && backup.ExecuteScalar<int>("SELECT COUNT(*) FROM cq_Tasks") == 0, "Late migration backs up the original blank database");

        using (var rolledBack = new SQLite.SQLiteConnection(database, SQLite.SQLiteOpenFlags.ReadOnly))
            check(rolledBack.ExecuteScalar<int>("SELECT COUNT(*) FROM cq_Tasks") == 0, "Interrupted late migration leaves SQLite unchanged");

        var repository = new SqlitePlayerRepository(database, source);
        service = new SqlitePlayerService(repository, new SystemTaskClock());
        try {
            check((await service.InitializeAsync()).Succeeded && service.PlayerName == "Returning player"
                && service.Experience == 550 && service.FishCoins == 123
                && service.TaskSnapshot.tasks.Any(task => task.id == "legacy-task"), "Late retry imports JSON progress and restores an account-only name");
            var saved = await repository.ReadAllForVerificationAsync();
            check(saved.Succeeded && saved.Value.username == "Returning player"
                && File.ReadAllText(source) == original
                && File.ReadAllText(database + ".pre-sqlite.json") == original, "Restored name is durable and both JSON copies retain their original contents");
        } finally { await service.CloseAsync(); }

        File.WriteAllText(source, "{\"username\":\"Stale JSON\",\"fishCoins\":900}");
        repository = new SqlitePlayerRepository(database, source);
        try {
            check((await repository.InitializeAsync()).Succeeded, "Late migration can be reopened without replay");
            var saved = await repository.ReadAllForVerificationAsync();
            check(saved.Succeeded && saved.Value.username == "Returning player"
                && saved.Value.fishCoins == 123 && saved.Value.tasks.Count == 1, "Reopening keeps SQLite authoritative over stale JSON");
        } finally { await repository.CloseAsync(); }

        string usedSource = Path.Combine(folder, "used-sqlite.json");
        database = Path.ChangeExtension(usedSource, ".sqlite3");
        repository = new SqlitePlayerRepository(database, usedSource);
        try {
            check((await repository.InitializeAsync()).Succeeded, "Independent unnamed SQLite fixture initializes");
            check((await repository.ExecuteAsync(tx => {
                tx.Put(PlayerRecordKind.Tasks, new QuestTask { id = "sqlite-task", title = "Keep this SQLite task" });
                return true;
            })).Succeeded, "Unnamed SQLite fixture gains its own task");
        } finally { await repository.CloseAsync(); }

        File.WriteAllText(usedSource, original);
        repository = new SqlitePlayerRepository(database, usedSource);
        try {
            check((await repository.InitializeAsync()).Succeeded, "SQLite with its own progress opens alongside JSON");
            var saved = await repository.ReadAllForVerificationAsync();
            check(saved.Succeeded && saved.Value.tasks.Single().id == "sqlite-task"
                && saved.Value.fishCoins == 0 && File.ReadAllText(usedSource) == original, "Late JSON never replaces existing SQLite progress");
        } finally { await repository.CloseAsync(); }
    }

    static async Task VerifyLegacyFocusPrecision(string folder, Action<bool, string> check)
    {
        const string focusId = "legacy-fractional-focus";
        const string completedUtc = "2026-11-16T04:00:00Z";
        string filename = "legacy-focus-precision.sqlite3";
        string path = Path.Combine(folder, filename);
        string expected;
        var original = new SqlitePlayerRepository(path);
        try {
            check((await original.InitializeAsync()).Succeeded, "Fractional Focus fixture initializes");
            check((await original.ExecuteAsync(tx => {
                tx.Put(PlayerRecordKind.FocusSessions, new FocusSession {
                    id = focusId, status = FocusSessionStatus.Completed, rewardDay = "2026-11-16",
                    completedUtc = completedUtc, elapsedSeconds = 1000.0005, eligibleSeconds = 941.3170427797909
                });
                return true;
            })).Succeeded, "Fractional Focus fixture saves a completed session");
            var saved = await original.ReadAllForVerificationAsync();
            check(saved.Succeeded, "Fractional Focus fixture can be read");
            expected = JsonUtility.ToJson(saved.Value);
        } finally { await original.CloseAsync(); }

        using (var db = new SQLite.SQLiteConnection(path)) {
            var payload = db.ExecuteScalar<string>("SELECT payload FROM cq_FocusSessions WHERE id=?", focusId);
            var session = JsonUtility.FromJson<FocusSession>(payload);
            double legacySeconds = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(session.eligibleSeconds) - 1);
            long indexedBegin = db.ExecuteScalar<long>("SELECT beginTicks FROM cq_FocusSessions WHERE id=?", focusId);
            long end = db.ExecuteScalar<long>("SELECT endTicks FROM cq_FocusSessions WHERE id=?", focusId);
            // A legacy index could round a half-millisecond boundary in either direction.
            long earlierBegin = end - 1000001 * TimeSpan.TicksPerMillisecond;
            long laterBegin = end - 1000000 * TimeSpan.TicksPerMillisecond;
            check(indexedBegin == earlierBegin || indexedBegin == laterBegin, "Focus fixture straddles a millisecond rounding boundary");
            db.Execute("UPDATE cq_FocusSessions SET eligibleSeconds=?,beginTicks=? WHERE id=?", legacySeconds,
                indexedBegin == earlierBegin ? laterBegin : earlierBegin, focusId);
            db.Execute("UPDATE cq_totals SET focusSeconds=? WHERE id=1", legacySeconds);
            db.Execute("DROP TABLE cq_mail_state");
            db.Execute("PRAGMA user_version=2");
        }

        var upgraded = new SqlitePlayerRepository(path);
        try {
            var initialized = await upgraded.InitializeAsync();
            check(initialized.Succeeded, "Schema-two save with Focus rounding differences upgrades: " + initialized.Message);
            var saved = await upgraded.ReadAllForVerificationAsync();
            check(saved.Succeeded && JsonUtility.ToJson(saved.Value) == expected, "Focus precision upgrade preserves all saved records");
            check((await upgraded.ExportBackupAsync(Path.Combine(folder, "fractional-focus.catquestbackup"))).Succeeded,
                "Upgraded fractional Focus save passes full backup validation");
        } finally { await upgraded.CloseAsync(); }

        string backup = Directory.GetFiles(folder, filename + ".before-schema-2-to-3-*.sqlite3").Single();
        using (var db = new SQLite.SQLiteConnection(backup, SQLite.SQLiteOpenFlags.ReadOnly))
            check(db.ExecuteScalar<int>("PRAGMA user_version") == 2 && db.ExecuteScalar<string>("PRAGMA integrity_check") == "ok",
                "Pre-upgrade fractional Focus snapshot remains a healthy schema-two backup");
        var reopened = new SqlitePlayerRepository(path);
        try {
            check((await reopened.InitializeAsync()).Succeeded, "Upgraded fractional Focus save reopens after closing");
            var saved = await reopened.ReadAllForVerificationAsync();
            check(saved.Succeeded && JsonUtility.ToJson(saved.Value) == expected, "Reopened Focus save preserves the original player records");
        } finally { await reopened.CloseAsync(); }
    }

    static async Task VerifyDerivedIndexes(string folder,Action<bool,string> check)
    {
        var source=new SqlitePlayerRepository(Path.Combine(folder,"derived-source.sqlite3"));
        var target=new SqlitePlayerRepository(Path.Combine(folder,"derived-target.sqlite3"));
        try {
            check((await source.InitializeAsync()).Succeeded&&(await target.InitializeAsync()).Succeeded,"Derived-index isolated fixtures initialize");
            check((await source.ExecuteAsync(tx=>{
                for(int i=0;i<121;i++) {
                    tx.Put(PlayerRecordKind.History,new TaskCompletion{id="derived-history-"+i,taskId="deleted-"+i,day="once",
                        completedLocalDay="2026-11-16",rewardedLocalDay="2026-11-16",completedUtc=i==0?null:"2026-11-16T04:00:00Z",
                        experience=7,fishCoins=3,tickets=1,snapshot=new QuestTask{id="deleted-"+i,title="Preserved receipt",tagIds=new List<string>{"snapshot-tag"}}});
                    tx.Put(PlayerRecordKind.FocusSessions,new FocusSession{id="derived-focus-"+i,status=FocusSessionStatus.Completed,
                        rewardDay="2026-11-16",eligibleMinutes=2,elapsedSeconds=150,completedUtc="2026-11-16T04:00:00Z",tagIds=new List<string>{"focus-tag"}});
                }
                return true;
            })).Succeeded,"Seed more than one index-validation batch with legacy eligibleMinutes fallback and null completion time");
            string portable=Path.Combine(folder,"derived-pristine.catquestbackup");
            check((await source.ExportBackupAsync(portable)).Succeeded,"Consistent legacy-shaped metadata passes full validation");
            string original=JsonUtility.ToJson((await target.ReadAllForVerificationAsync()).Value);
            string[] changes={
                "UPDATE cq_History SET rewardDay='2020-01-01' WHERE id='derived-history-120'",
                "UPDATE cq_History SET occurrenceDay='wrong' WHERE id='derived-history-120'",
                "UPDATE cq_History SET day='2020-01-01' WHERE id='derived-history-120'",
                "UPDATE cq_History SET parentId='wrong' WHERE id='derived-history-120'",
                "UPDATE cq_History SET state=1 WHERE id='derived-history-120'",
                "UPDATE cq_History SET experience=8 WHERE id='derived-history-120'",
                "UPDATE cq_History SET fishCoins=4 WHERE id='derived-history-120'",
                "UPDATE cq_History SET tickets=2 WHERE id='derived-history-120'",
                "UPDATE cq_History SET calendarDay='2020-01-01' WHERE id='derived-history-120'",
                "UPDATE cq_History SET sortKey=X'0102' WHERE id='derived-history-120'",
                "UPDATE cq_FocusSessions SET state=3 WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET eligibleSeconds=121 WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET rewardDay='2020-01-01' WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET day='2020-01-01' WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET parentId='wrong' WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET beginTicks=beginTicks+1 WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET beginTicks=beginTicks+10000 WHERE id='derived-focus-120'",
                "UPDATE cq_FocusSessions SET endTicks=NULL WHERE id='derived-focus-120'",
                "DELETE FROM cq_focus_tags WHERE sessionId='derived-focus-120'"
            };
            for(int i=0;i<changes.Length;i++) {
                string altered=Path.Combine(folder,"derived-tampered-"+i+".catquestbackup");File.Copy(portable,altered);
                using(var db=new SQLite.SQLiteConnection(altered))db.Execute(changes[i]);
                byte[] bytes=File.ReadAllBytes(altered);
                var result=await target.ImportBackupAsync(altered);
                check(!result.Succeeded&&result.Error==PlayerStorageError.Corrupt,"Reject inconsistent derived index metadata case "+i);
                check(File.ReadAllBytes(altered).SequenceEqual(bytes)&&JsonUtility.ToJson((await target.ReadAllForVerificationAsync()).Value)==original,"Rejected metadata preserves source and target case "+i);
            }
        } finally {await source.CloseAsync();await target.CloseAsync();}
    }

}
