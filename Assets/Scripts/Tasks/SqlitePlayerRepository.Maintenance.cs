using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class SqlitePlayerRepository
{
    DateTime lastRecoveryUtc = DateTime.UtcNow;
    long lastRecoveryRevision = -1;
    CancellationTokenSource maintenanceCancellation;
    Task<OperationResult<bool>> maintenanceTask;
    public Task<OperationResult<bool>> MaintainAsync()
    {
        lock (queueLock) {
            if (closing || !initialized || DatabasePath == ":memory:" || !tail.IsCompleted || maintenanceTask != null && !maintenanceTask.IsCompleted
                || DateTime.UtcNow - lastRecoveryUtc < TimeSpan.FromMinutes(5))
                return Task.FromResult(new OperationResult<bool> { Succeeded = true, Value = false });
            maintenanceCancellation?.Dispose();
            maintenanceCancellation = new CancellationTokenSource();
            var cancellation = maintenanceCancellation.Token;
            // This task never accesses the writer connection and is not appended
            // to tail: user commands continue while the read snapshot is copied.
            maintenanceTask = Task.Run(() => MaintainSnapshot(cancellation));
            return maintenanceTask;
        }
    }
    OperationResult<bool> MaintainSnapshot(CancellationToken cancellation)
    {
        try {
            cancellation.ThrowIfCancellationRequested();
            long revision;
            string folder = Path.GetDirectoryName(DatabasePath);
            string prefix = Path.GetFileName(DatabasePath) + ".recovery-";
            string path = Path.Combine(folder, prefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".sqlite3");
            using (var source = Open(DatabasePath, true)) {
                source.Execute("BEGIN");
                // The first read pins a WAL snapshot. Its revision must be used,
                // not the writer's revision after newer commands have committed.
                revision = long.Parse(SqlitePlayerSchema.MetaValue(source,"revision"), System.Globalization.CultureInfo.InvariantCulture);
                if (revision == lastRecoveryRevision) {
                    lock (queueLock) lastRecoveryUtc = DateTime.UtcNow;
                    return new OperationResult<bool> { Succeeded = true, Value = false, Version = revision };
                }
                FaultPoint?.Invoke("maintenance-snapshot");
                CreateConsistentBackup(source, path, cancellation);
                source.Execute("ROLLBACK");
            }
            lock (queueLock) { lastRecoveryRevision = revision; lastRecoveryUtc = DateTime.UtcNow; }
            // Rotate only finished snapshots. Pre-import and migration evidence
            // never matches this exact prefix. A cancelled copy keeps old backups.
            foreach (string prior in Directory.GetFiles(folder, prefix + "*.sqlite3").OrderByDescending(p => p, StringComparer.Ordinal).Skip(3)) {
                cancellation.ThrowIfCancellationRequested();
                string full = Path.GetFullPath(prior);
                if (Path.GetDirectoryName(full).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal)) File.Delete(full);
            }
            lock (queueLock) {
                if (!closing && tail.IsCompleted)
                    _ = Queue(() => { database.ExecuteScalar<int>("PRAGMA wal_checkpoint(PASSIVE)"); return true; });
            }
            return new OperationResult<bool> { Succeeded = true, Value = true, Version = revision };
        } catch (OperationCanceledException) {
            return new OperationResult<bool> { Succeeded = true, Value = false };
        } catch (Exception error) { return Failure<bool>(error); }
    }
}
