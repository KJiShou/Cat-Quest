using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SQLite;
using UnityEngine;

public sealed partial class SqlitePlayerRepository
{
    [Serializable] sealed class MigrationState { public int format = 1; public string phase, sourceHash, expectedHash; }
    [Serializable] sealed class LegacyHeader { public int version; }
    string Marker => DatabasePath + ".migration.json";
    string Staging => DatabasePath + ".migrating";
    static string Digest(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
    static string DataDigest(PlayerData data) => Digest(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
    static bool MatchesJournalDigest(PlayerData data, string expected)
    {
        string json = JsonUtility.ToJson(data);
        if (Digest(Encoding.UTF8.GetBytes(json)) == expected) return true;
        // An older in-progress migration hashed reward days before mission Tickets were recorded.
        if (data.rewardDays.Any(day => day.questsTickets != 0)) return false;
        string legacyJson = json.Replace(",\"questsTickets\":0", "");
        return Digest(Encoding.UTF8.GetBytes(legacyJson)) == expected;
    }
    static void AtomicText(string path, string value)
    {
        string temporary = path + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        if (!File.Exists(path)) {
            File.Move(temporary, path);
            return;
        }

        byte[] previous = File.ReadAllBytes(path);
        for (int attempt = 0; ; attempt++) {
            try {
                File.Replace(temporary, path, null);
                return;
            } catch (IOException) when (attempt < 3 && File.Exists(temporary) && File.Exists(path)) {
                // A short lock from antivirus or indexing can interrupt the atomic rename.
                System.Threading.Thread.Sleep(25 << attempt);
                if (!File.ReadAllBytes(path).SequenceEqual(previous) || !File.ReadAllBytes(temporary).SequenceEqual(bytes))
                    throw;
            }
        }
    }
    void Mark(MigrationState state) => AtomicText(Marker, JsonUtility.ToJson(state));
    void InitializeStorage()
    {
        if (DatabasePath != ":memory:" && File.Exists(DatabasePath + ".recovery-pending.json"))
            throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "Recovery was interrupted. Select a verified SQLite snapshot to finish recovery. All previous files have been kept.");
        if (DatabasePath == ":memory:") {
            database = Open(DatabasePath);
            database.RunInTransaction(() => { SqlitePlayerSchema.Create(database); SqlitePlayerSchema.WriteAll(database, Fresh()); });
            return;
        }
        MigrationState state = null;
        if (File.Exists(Marker)) {
            try { state = JsonUtility.FromJson<MigrationState>(File.ReadAllText(Marker)); }
            catch { throw new PlayerRepositoryException(PlayerStorageError.Migration, "The migration journal needs recovery. Your files have been kept."); }
            if (state == null || state.format != 1 || !new[] { "importing", "ready", "complete" }.Contains(state.phase))
                throw new PlayerRepositoryException(PlayerStorageError.Migration, "The migration journal is unsupported.");
        }
        if (File.Exists(DatabasePath)) {
            using (var existing = Open(DatabasePath)) {
                SqlitePlayerSchema.ValidateDatabase(existing, false, true);
                UpgradeStorage(existing);
                ImportLegacyIntoEmptyDatabase(existing, state);
            }
            Mark(new MigrationState { phase = "complete", sourceHash = state?.sourceHash, expectedHash = state?.expectedHash });
            return;
        }
        if (state?.phase == "complete") throw new PlayerRepositoryException(PlayerStorageError.Corrupt, "The migrated database is missing. Restore a SQLite backup; the old JSON is not your current progress.");
        string frozen = DatabasePath + ".pre-sqlite.json";
        if (state != null && FrozenDigest(frozen) != state.sourceHash)
            throw new PlayerRepositoryException(PlayerStorageError.Migration, "The frozen migration source changed. Your files have been kept for recovery.");
        if (state?.phase == "ready" && File.Exists(Staging)) {
            using (var staged = Open(Staging, true)) {
                SqlitePlayerSchema.ValidateDatabase(staged, true, true);
                if (!MatchesJournalDigest(SqlitePlayerSchema.ReadAll(staged), state.expectedHash)) throw new PlayerRepositoryException(PlayerStorageError.Migration, "The staged migration did not pass verification.");
            }
            Publish(state);
            return;
        }
        // The source is frozen once, before any destination schema/data is written.
        // Recovery reads the frozen files, not potentially changed old-version files.
        if (state == null) {
            FreezeLegacy(legacyPath, frozen);
            FreezeLegacy(legacyPath == null ? null : legacyPath + ".bak", frozen + ".bak");
        }
        var data = ReadLegacy(frozen, allowMissing: state == null || state.sourceHash == "new");
        string expected = DataDigest(data);
        if (state != null && expected != state.expectedHash && !MatchesJournalDigest(data, state.expectedHash))
            throw new PlayerRepositoryException(PlayerStorageError.Migration, "The migration source no longer matches its verified snapshot.");
        state = new MigrationState { phase = "importing", sourceHash = FrozenDigest(frozen), expectedHash = expected };
        Mark(state);
        FaultPoint?.Invoke("migration-journal");
        // Only the known, unpublished staging file is discarded on an incomplete
        // import; never the original JSON, its backups, or a published database.
        if (File.Exists(Staging)) File.Delete(Staging);
        if (File.Exists(Staging + "-journal")) File.Delete(Staging + "-journal");
        using (var staged = Open(Staging)) {
            staged.RunInTransaction(() => {
                SqlitePlayerSchema.Create(staged);
                SqlitePlayerSchema.WriteAll(staged, data);
                SqlitePlayerSchema.SetMeta(staged, "sourceHash", state.sourceHash);
                FaultPoint?.Invoke("migration-rows");
            });
            SqlitePlayerSchema.ValidateDatabase(staged, true, true);
            if (DataDigest(SqlitePlayerSchema.ReadAll(staged)) != state.expectedHash)
                throw new PlayerRepositoryException(PlayerStorageError.Migration, "Migration changed player data. The original save has been kept.");
        }
        state.phase = "ready"; Mark(state);
        FaultPoint?.Invoke("migration-ready");
        Publish(state);
    }
    // An earlier release may have started before the old JSON was restored to this folder.
    // Only the database created without a source and containing startup defaults can be replaced.
    void ImportLegacyIntoEmptyDatabase(SQLiteConnection existing, MigrationState state)
    {
        if (state?.sourceHash != "new" || legacyPath == null) return;

        string importedDataHash = SqlitePlayerSchema.MetaValue(existing, "lateLegacyDataHash");
        if (importedDataHash != null) {
            state.sourceHash = SqlitePlayerSchema.MetaValue(existing, "sourceHash");
            state.expectedHash = importedDataHash;
            return;
        }

        string frozen = DatabasePath + ".pre-sqlite.json";
        if (!File.Exists(legacyPath) && !File.Exists(legacyPath + ".bak")
            && !File.Exists(frozen) && !File.Exists(frozen + ".bak")) return;
        if (SqlitePlayerSchema.MetaValue(existing, "sourceHash") != "new") return;

        var profile = Query<PlayerData>(existing, new PlayerRecordQuery { Kind = PlayerRecordKind.Profile, Limit = 1 }).Single();
        if (!string.IsNullOrWhiteSpace(profile.username)) return;

        var current = SqlitePlayerSchema.ReadAll(existing);
        long revision = long.Parse(SqlitePlayerSchema.MetaValue(existing, "revision"), System.Globalization.CultureInfo.InvariantCulture);
        if (!IsStartupOnly(current)) return;

        string source = File.Exists(frozen) || File.Exists(frozen + ".bak") ? frozen : legacyPath;
        if (!HasLegacyName(ReadLegacy(source, allowMissing: false))) return;
        FreezeLegacy(legacyPath, frozen);
        FreezeLegacy(legacyPath + ".bak", frozen + ".bak");
        var imported = ReadLegacy(frozen, allowMissing: false);
        if (!HasLegacyName(imported)) return;

        string sourceHash = FrozenDigest(frozen);
        string expectedHash = DataDigest(imported);
        CreateConsistentBackup(existing, DatabasePath + ".before-late-legacy-" + Guid.NewGuid().ToString("N") + ".sqlite3",
            System.Threading.CancellationToken.None);
        existing.RunInTransaction(() => {
            if (SqlitePlayerSchema.MetaValue(existing, "revision") != revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || !IsStartupOnly(SqlitePlayerSchema.ReadAll(existing)))
                throw new PlayerRepositoryException(PlayerStorageError.Busy, "The save changed during migration. Restart Cat Quest to retry.");

            foreach (var map in SqlitePlayerSchema.Mappings) existing.Execute("DELETE FROM " + SqlitePlayerSchema.Table(map.Kind));
            SqlitePlayerSchema.WriteAll(existing, imported);
            if (DataDigest(SqlitePlayerSchema.ReadAll(existing)) != expectedHash)
                throw new PlayerRepositoryException(PlayerStorageError.Migration, "Migration changed player data. The original save has been kept.");

            SqlitePlayerSchema.SetMeta(existing, "revision", checked(revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            SqlitePlayerSchema.SetMeta(existing, "sourceHash", sourceHash);
            SqlitePlayerSchema.SetMeta(existing, "lateLegacyDataHash", expectedHash);
            FaultPoint?.Invoke("late-legacy-before-commit");
        });
        state.sourceHash = sourceHash;
        state.expectedHash = expectedHash;
    }

    static bool IsStartupOnly(PlayerData current)
    {
        var fresh = Fresh();
        // Startup may seed the stock tags. All other data must still match a fresh profile.
        if (current.tagsInitialized && current.tags.Select(tag => tag.name).SequenceEqual(TaskService.DefaultTags)
            && current.tags.All(tag => tag.colorHex == TagColors.DefaultHex)) {
            fresh.tagsInitialized = true;
            fresh.tags = current.tags;
        }
        return DataDigest(current) == DataDigest(fresh);
    }

    static bool HasLegacyName(PlayerData data) => !string.IsNullOrWhiteSpace(data.username)
        || !string.IsNullOrWhiteSpace(data.account?.playerName);

    void UpgradeStorage(SQLiteConnection connection)
    {
        int version = connection.ExecuteScalar<int>("PRAGMA user_version");
        if (version >= SqlitePlayerSchema.Version) return;
        CreateConsistentBackup(connection, DatabasePath + $".before-schema-{version}-to-{SqlitePlayerSchema.Version}-"
            + Guid.NewGuid().ToString("N") + ".sqlite3", System.Threading.CancellationToken.None);
        FaultPoint?.Invoke("schema-backup");
        SqlitePlayerSchema.UpgradePrevious(connection, () => FaultPoint?.Invoke("schema-upgrade-rows"));
    }

    static string FrozenDigest(string primary)
    {
        bool main = File.Exists(primary), backup = File.Exists(primary + ".bak");
        if (!main && !backup) return "new";
        string primaryDigest = main ? Digest(File.ReadAllBytes(primary)) : "missing";
        string backupDigest = backup ? Digest(File.ReadAllBytes(primary + ".bak")) : "missing";
        return Digest(Encoding.UTF8.GetBytes("primary:" + primaryDigest + "|backup:" + backupDigest));
    }
    void Publish(MigrationState state)
    {
        File.Move(Staging, DatabasePath);
        FaultPoint?.Invoke("migration-published");
        using (var check = Open(DatabasePath, true)) SqlitePlayerSchema.ValidateDatabase(check, true, true);
        state.phase = "complete"; Mark(state);
    }
    static void FreezeLegacy(string source, string destination)
    {
        if (source == null || !File.Exists(source) || File.Exists(destination)) return;
        using (var read = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var write = new FileStream(destination + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { read.CopyTo(write); write.Flush(true); }
        File.Move(destination + ".tmp", destination);
    }
    static PlayerData Fresh()
    {
        var data = new PlayerData { tutorial = new TutorialProgress { version = 1 } };
        PlayerDataStore.Normalize(data); return data;
    }
    internal static PlayerData ReadLegacy(string path, bool allowMissing)
    {
        bool hasPrimary = File.Exists(path), hasBackup = File.Exists(path + ".bak");
        if (!hasPrimary && !hasBackup) {
            if (allowMissing) return Fresh();
            throw new PlayerRepositoryException(PlayerStorageError.Migration, "The original save is missing. No new save was created.");
        }
        string primary = hasPrimary ? File.ReadAllText(path) : null;
        string backup = hasBackup ? File.ReadAllText(path + ".bak") : null;
        int Version(string json) {
            if (string.IsNullOrWhiteSpace(json)) return 0;
            try { return JsonUtility.FromJson<LegacyHeader>(json)?.version ?? 0; } catch { return 0; }
        }
        if (Math.Max(Version(primary), Version(backup)) > 6)
            throw new PlayerRepositoryException(PlayerStorageError.NewerVersion, "The JSON save or backup needs a newer version of Cat Quest.");
        PlayerData Parse(string json) {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{") || !json.TrimEnd().EndsWith("}")) return null;
            PlayerData data;
            try { data = JsonUtility.FromJson<PlayerData>(json); }
            catch (ArgumentException) { return null; }
            // Only unreadable JSON may fall back to the older backup. A readable
            // primary with conflicting identities must stop, not lose progress.
            if (data == null) return null;
            ValidateData(data);
            PlayerDataStore.Normalize(data);
            ValidateData(data);
            return data;
        }
        var selected = Parse(primary) ?? Parse(backup);
        return selected ?? throw new PlayerRepositoryException(PlayerStorageError.Migration, "The JSON save and backup could not be migrated. Your originals have been kept.");
    }
    internal static void ValidateData(PlayerData data)
    {
        if (data == null || data.version > 6 || data.version < 0 || data.experience < 0 || data.fishCoins < 0 || data.tickets < 0)
            throw new ArgumentException("Invalid player profile.");
        foreach (var map in SqlitePlayerSchema.Mappings.Where(m => !m.Singleton)) {
            var values = typeof(PlayerData).GetField(map.Field).GetValue(data) as IEnumerable;
            if (values == null) continue;
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values) {
                var row = SqlitePlayerSchema.Encode(map, value, 0);
                if (!ids.Add(row.id)) throw new ArgumentException("Duplicate identity.");
            }
        }
        void ValidateTask(QuestTask task) {
            if (task == null || string.IsNullOrWhiteSpace(task.id) || string.IsNullOrWhiteSpace(task.title) || !Enum.IsDefined(typeof(TaskDifficulty), task.difficulty)) throw new ArgumentException("Invalid task.");
            if ((task.daily || task.scheduled) && !TaskService.TryDate(task.date, out _)) throw new ArgumentException("Invalid task date.");
            if (task.daily && !TaskRepeat.ValidRule(task)) throw new ArgumentException("Invalid repeat rule.");
            if (task.subtasks != null && (task.subtasks.Any(s => s == null || string.IsNullOrWhiteSpace(s.id)) || task.subtasks.Select(s => s.id).Distinct().Count() != task.subtasks.Count)) throw new ArgumentException("Invalid subtasks.");
            if (task.tagIds != null && (task.tagIds.Any(string.IsNullOrWhiteSpace) || task.tagIds.Distinct().Count() != task.tagIds.Count)) throw new ArgumentException("Invalid tag associations.");
        }
        bool DateOrOnce(string day) => day == "once" || TaskService.TryDate(day, out _);
        bool OptionalDay(string day) => string.IsNullOrEmpty(day) || TaskService.TryDate(day, out _);
        bool Nonnegative(params int[] numbers) => numbers.All(n => n >= 0);
        bool Duration(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        foreach (var task in data.tasks ?? new System.Collections.Generic.List<QuestTask>()) ValidateTask(task);
        foreach (var occurrence in data.occurrences ?? new System.Collections.Generic.List<TaskOccurrence>())
            if (!DateOrOnce(occurrence.day)) throw new ArgumentException("Invalid occurrence date.");
        var completionIdentities = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var receipt in data.history ?? new System.Collections.Generic.List<TaskCompletion>()) {
            ValidateTask(receipt.snapshot);
            if (string.IsNullOrWhiteSpace(receipt.taskId) || !DateOrOnce(receipt.day) || !OptionalDay(receipt.completedLocalDay)
                || !OptionalDay(receipt.rewardedLocalDay) || !Nonnegative(receipt.experience, receipt.fishCoins, receipt.tickets)
                || !completionIdentities.Add(SqlitePlayerSchema.OccurrenceId(receipt.taskId, receipt.day))) throw new ArgumentException("Invalid completion receipt.");
        }
        foreach (var day in data.rewardDays ?? new System.Collections.Generic.List<RewardDay>())
            if (!TaskService.TryDate(day.day, out _) || !Nonnegative(day.taskExperience, day.taskFishCoins, day.focusMinutes, day.focusTickets,
                day.questsExperience, day.questsFishCoins, day.questsTickets, day.focusGoalExperience, day.focusGoalFishCoins, day.taskTickets, day.firstQuestExperience, day.firstQuestFishCoins)) throw new ArgumentException("Invalid daily reward record.");
        foreach (var session in data.focusSessions ?? new System.Collections.Generic.List<FocusSession>()) {
            if (!Enum.IsDefined(typeof(FocusSessionStatus), session.status) || !Enum.IsDefined(typeof(FocusTimerMode), session.mode)
                || !Duration(session.elapsedSeconds) || !Duration(session.eligibleSeconds) || !OptionalDay(session.rewardDay)
                || !Nonnegative(session.rewardLevel, session.focusDurationSeconds, session.restDurationSeconds, session.eligibleMinutes,
                    session.rewardedMinutes, session.rawTickets, session.experience, session.fishCoins, session.tickets)) throw new ArgumentException("Invalid Focus receipt.");
        }
        TaskService.CatLevel(data.experience, out int level, out _, out _);
        foreach (var item in data.equipment ?? new System.Collections.Generic.List<EquipmentItem>())
            if (EquipmentRules.Find(item.id) == null || item.level < 1 || item.level > level) throw new ArgumentException("Invalid equipment.");
        foreach (var upgrade in data.equipmentUpgrades ?? new System.Collections.Generic.List<EquipmentUpgradeReceipt>())
            if (EquipmentRules.Find(upgrade.itemId) == null || upgrade.fromLevel < 1) throw new ArgumentException("Invalid equipment upgrade receipt.");
        foreach (var progress in data.bossProgress ?? new System.Collections.Generic.List<BossProgress>())
            if (progress.bossId < 1 || progress.bossId > 3 || !Nonnegative(progress.wins, progress.pendingClaims, progress.claimedComponents)
                || progress.pendingClaims > 3 || progress.claimedComponents > 3) throw new ArgumentException("Invalid boss progression.");
        foreach (var battle in data.battles ?? new System.Collections.Generic.List<BattleAttempt>()) {
            if (battle.bossId < 1 || battle.bossId > 3 || !Enum.IsDefined(typeof(BattleOutcome), battle.outcome)
                || !Nonnegative(battle.masterStage, battle.experience, battle.fishCoins, battle.equipmentAbilityVersion)
                || battle.masterStage > 0 && battle.bossId != 1 + (battle.masterStage - 1) % 3) throw new ArgumentException("Invalid battle receipt.");
            if ((battle.outcome == BattleOutcome.Running || battle.outcome == BattleOutcome.Victory || battle.outcome == BattleOutcome.Defeat)
                && (!battle.cat.Valid || !battle.boss.Valid)) throw new ArgumentException("Invalid battle snapshot.");
        }
        foreach (var receipt in data.lootReceipts ?? new System.Collections.Generic.List<LootReceipt>())
            if ((receipt.count != 1 && receipt.count != 10) || receipt.drops == null || receipt.drops.Length != receipt.count
                || receipt.drops.Any(drop => drop == null || LootRules.Catalog.All(item => item.Id != drop.itemId) || drop.duplicateFish < 0)) throw new ArgumentException("Invalid chest receipt.");
    }
}
