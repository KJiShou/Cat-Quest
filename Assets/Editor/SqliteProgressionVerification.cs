using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

// Called by the root SQLite verifier, never automatically registered. All writes
// belong to the caller's ignored verification directory, never the live save.
public static class SqliteProgressionVerification
{
    sealed class Clock : ITaskClock
    {
        public DateTime LocalNow => new DateTime(2026, 11, 16, 12, 0, 0);
        public DateTime UtcNow => new DateTime(2026, 11, 16, 4, 0, 0, DateTimeKind.Utc);
    }
    sealed class MemoryStore : IPlayerDataStore
    {
        public PlayerData Data { get; private set; }
        public bool fail;
        public MemoryStore(PlayerData data) { Data = data; }
        public bool TrySave(PlayerData candidate, out string error)
        {
            error = fail ? "Injected save failure" : null;
            if (fail) return false;
            PlayerDataStore.Normalize(candidate); Data = candidate; return true;
        }
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        string folder = Path.GetFullPath(Path.Combine(root, "progression"));
        Directory.CreateDirectory(folder);
        string legacy = Path.Combine(folder, "legacy.json"), path = Path.Combine(folder, "player.sqlite3");
        if (File.Exists(path) || File.Exists(legacy)) throw new InvalidOperationException("Progression verification requires a fresh directory.");
        var fixture = new PlayerData { username = "SQLite progression", experience = 10000, fishCoins = 10000, tickets = 100,
            tagsInitialized = true, equipmentUnlocked = true, tutorial = new TutorialProgress { version = 1, dismissed = true } };
        PlayerDataStore.Normalize(fixture); fixture.bossProgress.Single(b => b.bossId == 1).wins = 4;
        PlayerDataStore.Normalize(fixture);
        File.WriteAllText(legacy, JsonUtility.ToJson(fixture));
        var repository = new SqlitePlayerRepository(path, legacy);
        var clock = new Clock();
        var service = new SqlitePlayerService(repository, clock);
        var memory = new MemoryStore(PlayerDataStore.Copy(fixture));
        var old = new TaskService(memory, clock);
        async Task Same(string label)
        {
            var snapshot = await repository.ReadAllForVerificationAsync();
            check(snapshot.Succeeded, label + ": SQLite read succeeds");
            check(JsonUtility.ToJson(snapshot.Value) == JsonUtility.ToJson(memory.Data), label + ": old/new complete state is equal");
        }
        async Task Match(bool expected, Task<OperationResult<bool>> operation, string label)
        {
            var result = await operation; check(result.Succeeded == expected, label + ": old/new acceptance is equal"); await Same(label);
        }
        try {
            check((await service.InitializeAsync()).Succeeded, "Progression service initializes isolated migrated data");
            await Same("Initialization");
            await Match(old.ClaimEquipment("boss1-weapon"), service.ClaimEquipmentAsync("boss1-weapon"), "Mastery claim before fifth win rejected");
            bool entered = old.BeginBattle(1, 0, out var attempt);
            var newAttempt = await service.BeginBattleAsync(1, 0, attempt?.id);
            check(entered && newAttempt.Succeeded, "Both implementations accept valid boss entry"); await Same("Atomic ticket entry");
            check((await service.BeginBattleAsync(1, 0, attempt.id)).Succeeded, "Repeated entry identity returns committed receipt"); await Same("Repeated entry spends no second ticket");
            bool won = old.ResolveBattle(attempt.id, BattleOutcome.Victory);
            check((await service.ResolveBattleAsync(attempt.id, BattleOutcome.Victory)).Succeeded == won, "Both resolve deterministic battle");
            await Same("Fifth victory and mastery allowance");
            check((await service.ResolveBattleAsync(attempt.id, BattleOutcome.Victory)).Succeeded, "Duplicate battle outcome accepted idempotently"); await Same("Duplicate outcome does not advance or pay again");
            await Match(old.ClaimEquipment("boss1-weapon"), service.ClaimEquipmentAsync("boss1-weapon"), "Fifth-win weapon claim");
            await Match(old.ClaimEquipment("boss1-weapon"), service.ClaimEquipmentAsync("boss1-weapon"), "Duplicate mastery component rejected");
            await Match(old.ClaimEquipment("boss1-body"), service.ClaimEquipmentAsync("boss1-body"), "Tenth-win armour remains locked");
            await Match(old.Equip("boss1-weapon", EquipmentSlot.Weapon), service.EquipAsync("boss1-weapon", EquipmentSlot.Weapon), "Equipment selection");
            memory.fail = true;
            repository.FaultPoint = stage => { if (stage == "before-commit") throw new IOException("Injected transaction failure"); };
            await Match(old.UpgradeEquipment("boss1-weapon", 1, "upgrade-1"), service.UpgradeEquipmentAsync("boss1-weapon", 1, "upgrade-1"), "Upgrade failure rolls back currency equipment and receipt");
            memory.fail = false; repository.FaultPoint = null;
            await Match(old.UpgradeEquipment("boss1-weapon", 1, "upgrade-1"), service.UpgradeEquipmentAsync("boss1-weapon", 1, "upgrade-1"), "Upgrade retry");
            await Match(old.UpgradeEquipment("boss1-weapon", 1, "upgrade-1"), service.UpgradeEquipmentAsync("boss1-weapon", 1, "upgrade-1"), "Upgrade duplicate request");
            await Match(old.UpgradeEquipment("boss1-weapon", 1, "upgrade-stale"), service.UpgradeEquipmentAsync("boss1-weapon", 1, "upgrade-stale"), "Stale expected level rejected");
            await Match(old.Unequip(EquipmentSlot.Weapon), service.UnequipAsync(EquipmentSlot.Weapon), "Unequip");
            await Match(old.AutoEquip(), service.AutoEquipAsync(), "Auto equip uses unchanged pure scoring");
            old.BeginBattle(1, 0, out var interrupted);
            check((await service.BeginBattleAsync(1, 0, interrupted.id)).Succeeded, "Interrupted-entry fixture entered");
            await Match(old.RecoverInterruptedBattles(), service.RecoverInterruptedBattlesAsync(), "Interrupted entry refunds exactly once");
            await Match(old.RecoverInterruptedBattles(), service.RecoverInterruptedBattlesAsync(), "Repeated recovery does not duplicate refund");

            var oldDraw = typeof(TaskService).GetMethod("DrawLoot", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(int), typeof(string), typeof(LootReceipt).MakeByRefType(), typeof(Func<int, int>) }, null);
            var newDraw = typeof(SqlitePlayerService).GetMethod("DrawLootAsync", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(int), typeof(string), typeof(Func<int, int>) }, null);
            Func<int, int> roll = _ => 0;
            var arguments = new object[] { 10, "draw-10", null, roll };
            bool oldDrawn = (bool)oldDraw.Invoke(old, arguments);
            var newDrawn = await (Task<OperationResult<LootReceipt>>)newDraw.Invoke(service, new object[] { 10, "draw-10", roll });
            check(oldDrawn == newDrawn.Succeeded, "Ten-chest acceptance matches"); await Same("Ten-chest debit drops and duplicate compensation");
            check((await service.DrawLootAsync(10, "draw-10")).Succeeded, "Duplicate chest request returns its receipt"); await Same("Duplicate chest request does not draw or pay again");
            check(!(await service.DrawLootAsync(1, "draw-10")).Succeeded, "Changed count cannot reuse committed chest request"); await Same("Mismatched chest request leaves state unchanged");

            await Match(old.RenamePlayer("  Research Cat  "), service.RenamePlayerAsync("  Research Cat  "), "Rename trim and account display name");
            var settings = old.Settings; settings.soundVolume = .25f; settings.reduceMotion = true;
            await Match(old.SaveSettings(settings), service.SaveSettingsAsync(settings), "Settings round trip");
            var account = old.AccountSettings; account.boundUid = "test-owner"; account.uploadEnabled = true; account.playerName = "Research Cat";
            await Match(old.SaveAccountSettings(account), service.SaveAccountSettingsAsync(account), "Account preference commit");
            var changedOwner = PlayerDataStore.Copy(account); changedOwner.boundUid = "another-owner";
            await Match(old.SaveAccountSettings(changedOwner), service.SaveAccountSettingsAsync(changedOwner), "Account ownership cannot be silently changed");
            var tutorial = old.Tutorial; tutorial.equipmentStep = 3;
            await Match(old.SaveTutorial(tutorial), service.SaveTutorialAsync(tutorial), "Tutorial progress commit");
            var firstExchange = Guid.NewGuid().ToString("N");
            var secondExchange = Guid.NewGuid().ToString("N");
            int fish = service.FishCoins, tickets = service.Tickets;
            repository.FaultPoint = stage => { if (stage == "before-commit") throw new IOException("Injected exchange failure"); };
            var failed = await service.ExchangeBossTicketAsync(3, "", firstExchange);
            check(!failed.Succeeded && service.FishCoins == fish && service.Tickets == tickets, "Failed exchange leaves both balances intact");
            repository.FaultPoint = null;
            var duplicates = await Task.WhenAll(service.ExchangeBossTicketAsync(3, "", firstExchange), service.ExchangeBossTicketAsync(3, "", firstExchange));
            check(duplicates.All(r => r.Succeeded) && service.FishCoins == fish - 300 && service.Tickets == tickets + 3,
                "Retry and duplicate exchange settle all three tickets once");
            check(!(await service.ExchangeBossTicketAsync(2, "", firstExchange)).Succeeded, "Committed request rejects a different quantity");
            check(!(await service.ExchangeBossTicketAsync(0, firstExchange, secondExchange)).Succeeded &&
                !(await service.ExchangeBossTicketAsync(service.MaxBossTicketExchange + 1, firstExchange, secondExchange)).Succeeded &&
                !(await service.ExchangeBossTicketAsync(int.MaxValue, firstExchange, secondExchange)).Succeeded &&
                service.FishCoins == fish - 300 && service.Tickets == tickets + 3, "Invalid, unaffordable and overflowing quantities cannot change balances");
            check((await service.ExchangeBossTicketAsync(2, firstExchange, secondExchange)).Succeeded &&
                service.FishCoins == fish - 500 && service.Tickets == tickets + 5, "Second exchange on the same day succeeds at the same unit price");
            check(!(await service.ExchangeBossTicketAsync(3, "", firstExchange)).Succeeded && service.Tickets == tickets + 5,
                "Stale exchange replay cannot charge again");
            memory.Data.fishCoins -= 500;
            memory.Data.tickets += 5;
            memory.Data.lastTicketExchangeId = secondExchange;
            memory.Data.lastTicketExchangeQuantity = 2;
            await Same("Ticket exchange profile and request identity");
            string bulkId = Guid.NewGuid().ToString("N");
            const string weapon = "boss1-weapon";
            int beforeUpgrade = service.FishCoins;
            int maximum = service.MaxEquipmentUpgradeLevel(weapon, 2);
            check(maximum >= 5 && EquipmentRules.UpgradeCost(2, 5) == 75 &&
                EquipmentRules.AffordableUpgradeLevel(2, 8, 74) == 4 && EquipmentRules.AffordableUpgradeLevel(2, 8, 75) == 5,
                "Batch pricing accumulates per-level costs and honors the exact affordability boundary");
            check(!(await service.UpgradeEquipmentAsync(weapon, 2, maximum + 1, bulkId)).Succeeded &&
                !(await service.UpgradeEquipmentAsync(weapon, 2, 2, bulkId)).Succeeded && service.FishCoins == beforeUpgrade,
                "Invalid and over-limit targets cannot spend fish");
            repository.FaultPoint = phase => { if (phase == "before-commit") throw new IOException("Injected bulk upgrade failure"); };
            check(!(await service.UpgradeEquipmentAsync(weapon, 2, 5, bulkId)).Succeeded && service.FishCoins == beforeUpgrade &&
                service.EquipmentSnapshot.equipment.Single(i => i.id == weapon).level == 2, "Failed bulk upgrade rolls back level and price");
            repository.FaultPoint = null;
            var batchRetries = await Task.WhenAll(service.UpgradeEquipmentAsync(weapon, 2, 5, bulkId),service.UpgradeEquipmentAsync(weapon, 2, 5, bulkId));
            check(batchRetries.All(result => result.Succeeded) && service.FishCoins == beforeUpgrade - 75 &&
                service.EquipmentSnapshot.equipment.Single(i => i.id == weapon).level == 5, "Concurrent retries commit the full batch once");
            check(!(await service.UpgradeEquipmentAsync(weapon, 2, 4, bulkId)).Succeeded, "Committed upgrade identity rejects a different target");
            memory.Data.fishCoins -= 75;
            memory.Data.equipment.Single(i => i.id == weapon).level = 5;
            memory.Data.equipmentUpgrades.Add(new EquipmentUpgradeReceipt { id=bulkId, itemId=weapon, fromLevel=2, toLevel=5 });
            await Same("Batch upgrade level, fish and receipt");
            await repository.CloseAsync();
            var reopened = new SqlitePlayerRepository(path, legacy);
            try {
                check((await reopened.InitializeAsync()).Succeeded, "Progression database reopens");
                var persisted = await reopened.ReadAllForVerificationAsync();
                check(persisted.Succeeded && JsonUtility.ToJson(persisted.Value) == JsonUtility.ToJson(memory.Data), "All progression results survive reload unchanged");
            } finally { await reopened.CloseAsync(); }
        } finally { repository.FaultPoint = null; await repository.CloseAsync(); }
    }
}
