using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SQLite;

public sealed partial class SqlitePlayerRepository
{
    // This entry point is deliberately unavailable during normal operation. The
    // caller must show the snapshot time and obtain an explicit recovery choice.
    public Task<OperationResult<bool>> RecoverFromSnapshotAsync(string path)
    {
        string sourcePath = Path.GetFullPath(path);
        return Queue(() => {
            if (initialized || DatabasePath == ":memory:" || string.Equals(sourcePath, DatabasePath, StringComparison.OrdinalIgnoreCase))
                throw new PlayerRepositoryException(PlayerStorageError.InvalidData, "Choose a separate SQLite recovery snapshot while the save is closed.");
            string token = Guid.NewGuid().ToString("N");
            string staged = DatabasePath + ".recovering-" + token;
            string preserved = DatabasePath + ".before-recovery-" + token;
            string pending = DatabasePath + ".recovery-pending.json";
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
            try {
                try { ownership = new FileStream(DatabasePath + ".owner", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { throw new PlayerRepositoryException(PlayerStorageError.Busy, "Close the other Cat Quest instance before recovering this save."); }
                if (File.Exists(DatabasePath)) {
                    try {
                        using var current = Open(DatabasePath, true);
                        if (current.ExecuteScalar<int>("PRAGMA user_version") > SqlitePlayerSchema.Version)
                            throw new PlayerRepositoryException(PlayerStorageError.NewerVersion, "Open this save with a newer Cat Quest version before recovering it.");
                    } catch (SQLiteException error) when (error.Result == SQLite3.Result.Corrupt || error.Result == SQLite3.Result.NonDBFile) { }
                }
                using (var source = Open(sourcePath, true)) {
                    source.Execute("BEGIN");
                    SqlitePlayerSchema.ValidateDatabase(source, true, true);
                    ValidateData(SqlitePlayerSchema.ReadAll(source));
                    database = source;
                    try { RecoveryBackup(staged); } finally { database = null; }
                }
                // Preserve byte-for-byte evidence before separating any sidecar.
                foreach (string suffix in new[] { "", "-wal", "-shm", ".migration.json", ".recovery-pending.json" })
                    if (File.Exists(DatabasePath + suffix)) File.Copy(DatabasePath + suffix, preserved + suffix, false);
                AtomicText(pending, "{\"phase\":\"publishing\",\"preserved\":\"" + token + "\"}");
                FaultPoint?.Invoke("recovery-before-publish");
                foreach (string suffix in new[] { "-wal", "-shm" }) if (File.Exists(DatabasePath + suffix)) File.Delete(DatabasePath + suffix);
                if (File.Exists(DatabasePath)) File.Replace(staged, DatabasePath, null); else File.Move(staged, DatabasePath);
                FaultPoint?.Invoke("recovery-after-publish");
                using (var verified = Open(DatabasePath, true)) SqlitePlayerSchema.ValidateDatabase(verified, true, true);
                Mark(new MigrationState { phase = "complete", sourceHash = "explicit-sqlite-recovery" });
                File.Delete(pending);
                return true;
            } finally {
                database = null;
                ownership?.Dispose(); ownership = null;
                if (File.Exists(staged)) File.Delete(staged);
            }
        }, false);
    }
    void CheckTransferPath(string path)
    {
        if (DatabasePath != ":memory:" && Path.GetFullPath(path).StartsWith(DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a file outside the active database and recovery files.");
        if (legacyPath != null && Path.GetFullPath(path).StartsWith(legacyPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a file outside the original JSON save.");
    }
    void CheckNoActiveSession()
    {
        if (database.ExecuteScalar<long>("SELECT count(*) FROM cq_FocusSessions WHERE state=?", (int)FocusSessionStatus.Running) != 0
            || database.ExecuteScalar<long>("SELECT count(*) FROM cq_Battles WHERE state=?", (int)BattleOutcome.Running) != 0)
            throw new PlayerRepositoryException(PlayerStorageError.Busy, "Finish Focus or battle before transferring a backup.");
    }
    public Task<OperationResult<string>> ExportBackupAsync(string destination)
    {
        string path = Path.GetFullPath(destination); CheckTransferPath(path);
        return Queue(() => {
            database.Execute("BEGIN");
            try {
            CheckNoActiveSession();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                // Construct a NEW database; excluded account fields never enter
                // its pages, freelist, journal or WAL, even temporarily.
                using (var output = Open(temporary)) {
                    output.RunInTransaction(() => {
                        SqlitePlayerSchema.Create(output);
                        foreach (var map in SqlitePlayerSchema.Mappings) {
                            if (map.Kind == PlayerRecordKind.Account) continue;
                            foreach (var row in database.DeferredQuery<SqlitePlayerSchema.Row>("SELECT * FROM " + SqlitePlayerSchema.Table(map.Kind) + " ORDER BY sequence"))
                                SqlitePlayerSchema.Put(output, map, UnityEngine.JsonUtility.FromJson(row.payload, map.Type), row.sequence);
                        }
                        var profile = Query<PlayerData>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.Profile, Limit = 1 }).Single();
                        SqlitePlayerSchema.Put(output, SqlitePlayerSchema.Get(PlayerRecordKind.Account), new AccountPreferences { playerName = profile.username });
                        SqlitePlayerSchema.SetMeta(output, "purpose", "portable-backup");
                        SqlitePlayerSchema.SetMeta(output, "exportedUtc", DateTime.UtcNow.ToString("O"));
                    });
                    SqlitePlayerSchema.ValidateDatabase(output, true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                return path;
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            } finally { database.Execute("ROLLBACK"); }
        });
    }
    public Task<OperationResult<bool>> ImportBackupAsync(string source)
    {
        string path = Path.GetFullPath(source); CheckTransferPath(path);
        return Queue(() => {
            CheckNoActiveSession();
            PlayerData imported;
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) imported = ReadLegacy(path, false);
            else {
                using (var input = Open(path, true)) {
                    input.Execute("BEGIN");
                    SqlitePlayerSchema.ValidateDatabase(input, true, true);
                    if (SqlitePlayerSchema.MetaValue(input, "purpose") != "portable-backup") throw new ArgumentException("Choose an exported Cat Quest backup.");
                    FaultPoint?.Invoke("import-source-validated");
                    imported = SqlitePlayerSchema.ReadAll(input);
                }
            }
            ValidateData(imported);
            if (imported.focusSessions.Any(s => s.status == FocusSessionStatus.Running) || imported.battles.Any(b => b.outcome == BattleOutcome.Running))
                throw new ArgumentException("The backup has an unfinished session.");
            imported.account = Query<AccountPreferences>(database, new PlayerRecordQuery { Kind = PlayerRecordKind.Account, Limit = 1 }).Single();
            imported.account.playerName = imported.username;
            if (DatabasePath != ":memory:") RecoveryBackup(DatabasePath + ".before-import-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            long revision = Revision;
            string importCommitId = Guid.NewGuid().ToString("N");
            try {
                database.Execute("BEGIN IMMEDIATE");
                foreach (var map in SqlitePlayerSchema.Mappings) database.Execute("DELETE FROM " + SqlitePlayerSchema.Table(map.Kind));
                database.Execute("UPDATE cq_totals SET taskCount=0,focusSeconds=0");
                SqlitePlayerSchema.WriteAll(database, imported);
                SqlitePlayerSchema.SetMeta(database, "revision", checked(revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                SqlitePlayerSchema.SetMeta(database, "importCommitId", importCommitId);
                FaultPoint?.Invoke("import-before-commit");
                database.Execute("COMMIT");
                FaultPoint?.Invoke("import-after-commit");
            } catch {
                try { database.Execute("ROLLBACK"); } catch (SQLiteException) { }
                // This marker was committed with the replacement. A lost commit
                // acknowledgement must never turn a successful import into retry.
                if (SqlitePlayerSchema.MetaValue(database,"importCommitId") != importCommitId) throw;
            }
            return true;
        });
    }
    void RecoveryBackup(string path)
        => CreateConsistentBackup(database, path, System.Threading.CancellationToken.None);
    static void CreateConsistentBackup(SQLiteConnection source, string path, System.Threading.CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            // SQLite's backup API includes committed WAL pages. A raw File.Copy
            // of DatabasePath would not represent the committed database.
            using (var target = Open(temporary)) {
                cancellation.ThrowIfCancellationRequested();
                var backup = SQLite3.BackupInit(target.Handle, "main", source.Handle, "main");
                if (backup == IntPtr.Zero) throw SQLiteException.New(SQLite3.GetResult(target.Handle), "Could not begin a recovery snapshot.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                long busyStarted = -1;
                bool completed = false;
                try {
                    while (!completed) {
                        cancellation.ThrowIfCancellationRequested();
                        if (timer.ElapsedMilliseconds > 60000) throw new PlayerRepositoryException(PlayerStorageError.Busy, "The recovery snapshot timed out. Your previous backups have been kept.");
                        var result = SQLite3.BackupStep(backup, 256);
                        if (result == SQLite3.Result.Done) completed = true;
                        else if (result == SQLite3.Result.OK) busyStarted = -1;
                        else if (result == SQLite3.Result.Busy || result == SQLite3.Result.Locked) {
                            if (busyStarted < 0) busyStarted = timer.ElapsedMilliseconds;
                            if (timer.ElapsedMilliseconds - busyStarted >= 3000) throw SQLiteException.New(result, "The recovery snapshot is busy.");
                            System.Threading.Thread.Sleep(10);
                        } else throw SQLiteException.New(result, "The recovery snapshot failed.");
                    }
                } finally {
                    var finished = SQLite3.BackupFinish(backup);
                    if (completed && finished != SQLite3.Result.OK) throw SQLiteException.New(finished, "The recovery snapshot did not finish.");
                }
                target.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                if (!string.Equals(target.ExecuteScalar<string>("PRAGMA journal_mode=DELETE"), "delete", StringComparison.OrdinalIgnoreCase))
                    throw new PlayerRepositoryException(PlayerStorageError.Busy, "The snapshot could not be finalized as a standalone file.");
            }
            using (var check = Open(temporary, true)) SqlitePlayerSchema.ValidateDatabase(check, true, true);
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        } finally {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(temporary + suffix)) File.Delete(temporary + suffix);
        }
    }
    public Task<OperationResult<string>> CreateRecoveryBackupAsync(string path)
    {
        string destination = Path.GetFullPath(path);
        return Queue(() => { CheckTransferPath(destination); RecoveryBackup(destination); return destination; });
    }
}
