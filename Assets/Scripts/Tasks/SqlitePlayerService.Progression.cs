using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

public sealed partial class SqlitePlayerService
{
    readonly Random progressionLootRandom = new Random();
    // Failed draws retain their selected items for the same request's retry. A
    // committed receipt is authoritative; this cache never grants any rewards.
    readonly Dictionary<string, string[]> pendingLootSelections = new Dictionary<string, string[]>(StringComparer.Ordinal);

    static PlayerData ProgressionProfile(IPlayerTransaction tx)
        => tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile") ?? throw new PlayerCommandException("The player profile is unavailable.");
    static bool ProgressionBattleActive(IPlayerTransaction tx)
        => tx.Query<BattleAttempt>(new PlayerRecordQuery { Kind = PlayerRecordKind.Battles, State = (int)BattleOutcome.Running, Limit = 1 }).Count != 0;
    static int ProgressionLevel(PlayerData profile)
    {
        TaskService.CatLevel(profile.experience, out int level, out _, out _);
        return level;
    }
    static PlayerData ProgressionEquipment(IPlayerTransaction tx)
    {
        var profile = ProgressionProfile(tx);
        // Bounded projection for the existing pure equipment/stat formulas. It
        // contains only the finite equipment catalog and three boss records.
        profile.equipment = tx.Equipment().ToList();
        profile.bossProgress = tx.Query<BossProgress>(new PlayerRecordQuery { Kind = PlayerRecordKind.BossProgress }).ToList();
        return profile;
    }
    static string ProgressionBossGate(PlayerData profile, int bossId, int masterStage)
    {
        int level = ProgressionLevel(profile);
        if (level < TaskService.RequiredLevel(ProgressionFeature.Boss)) return "Unlocks at Lv." + TaskService.RequiredLevel(ProgressionFeature.Boss);
        if (bossId < 1 || bossId > 3 || masterStage < 0) return "Invalid boss.";
        int Wins(int id) => profile.bossProgress.FirstOrDefault(p => p.bossId == id)?.wins ?? 0;
        if (masterStage > 0) {
            if (Wins(3) == 0) return "Defeat Boss 3 to unlock Master.";
            if (masterStage != profile.masterStage || bossId != 1 + (masterStage - 1) % 3) return "This Master stage is not available.";
        } else {
            if (bossId > 1 && Wins(bossId - 1) == 0) return "Defeat Boss " + (bossId - 1) + " first.";
            int required = bossId == 1 ? 1 : bossId == 2 ? 10 : 20;
            if (level < required) return "Requires Lv. " + required + ".";
        }
        return null;
    }
    static void ProgressionEquipmentAccess(IPlayerTransaction tx, PlayerData profile)
    {
        if (!profile.equipmentUnlocked) throw new PlayerCommandException("Claim your first Daily Reward to unlock equipment.");
        if (ProgressionBattleActive(tx)) throw new PlayerCommandException("Finish the current battle before changing equipment.");
    }
    static void ProgressionRequire(bool condition, string message)
    {
        if (!condition) throw new PlayerCommandException(message);
    }

    public Task<OperationResult<BattleAttempt>> BeginBattleAsync(int bossId, int masterStage, string requestId = null)
    {
        string id = requestId ?? Guid.NewGuid().ToString("N");
        string started = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return ExecuteCommandAsync(tx => {
            ProgressionRequire(!string.IsNullOrWhiteSpace(id) && id.Length <= 128, "Invalid battle request.");
            var prior = tx.Read<BattleAttempt>(PlayerRecordKind.Battles, id);
            if (prior != null) {
                ProgressionRequire(prior.bossId == bossId && prior.masterStage == masterStage, "Battle request does not match.");
                return prior;
            }
            ProgressionRequire(!ProgressionBattleActive(tx), "Finish the current battle first.");
            var profile = ProgressionEquipment(tx);
            string gate = ProgressionBossGate(profile, bossId, masterStage);
            ProgressionRequire(gate == null, gate);
            ProgressionRequire(profile.tickets >= 1, "Complete a task or use Focus to earn a ticket.");
            var attempt = new BattleAttempt { id = id, bossId = bossId, masterStage = masterStage,
                cat = EquipmentRules.Calculate(profile).Final, boss = BattleRules.Boss(bossId, masterStage),
                equipmentAbilityVersion = 2, startedUtc = started,
                equippedEquipment = profile.equipment.Where(item => Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>()
                    .Any(slot => EquipmentRules.Equipped(profile, slot) == item.id)).Select(item => new EquipmentItem { id = item.id, level = item.level }).ToList() };
            profile.tickets--;
            tx.Put(PlayerRecordKind.Profile, profile);
            tx.Put(PlayerRecordKind.Battles, attempt);
            return attempt;
        });
    }

    public Task<OperationResult<BattleAttempt>> ResolveBattleAsync(string id, BattleOutcome request)
    {
        string completed = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return ExecuteCommandAsync(tx => {
            var battle = tx.Read<BattleAttempt>(PlayerRecordKind.Battles, id);
            ProgressionRequire(battle != null, "Battle not found.");
            if (battle.outcome != BattleOutcome.Running) return battle;
            var outcome = request;
            if (request != BattleOutcome.Stopped && request != BattleOutcome.Error) {
                var simulation = new BattleSimulation(battle);
                simulation.Advance(120);
                outcome = simulation.Outcome;
            }
            var profile = ProgressionProfile(tx);
            int previousExperience = profile.experience;
            battle.outcome = outcome; battle.completedUtc = completed;
            if (outcome == BattleOutcome.Error) profile.tickets = checked(profile.tickets + 1);
            else if (outcome == BattleOutcome.Victory) {
                if (battle.masterStage > 0) {
                    ProgressionRequire(battle.masterStage == profile.masterStage, "Master stage has already changed.");
                    battle.experience = checked(50 + 10 * battle.masterStage);
                    battle.fishCoins = checked(25 + 5 * battle.masterStage);
                    profile.masterStage = checked(profile.masterStage + 1);
                } else {
                    var progress = tx.Read<BossProgress>(PlayerRecordKind.BossProgress, battle.bossId.ToString(CultureInfo.InvariantCulture));
                    ProgressionRequire(progress != null, "Boss progression is unavailable.");
                    bool first = progress.wins == 0;
                    progress.wins = checked(progress.wins + 1);
                    bool milestone = progress.wins == 5 || progress.wins == 10 || progress.wins == 15;
                    battle.experience = first ? (battle.bossId == 1 ? 30 : battle.bossId == 2 ? 60 : 100) : 10 * battle.bossId;
                    if (milestone) progress.pendingClaims++;
                    else if (progress.claimedComponents == 3) battle.fishCoins = 10 * battle.bossId;
                    tx.Put(PlayerRecordKind.BossProgress, progress);
                }
                profile.experience = checked(profile.experience + battle.experience);
                profile.fishCoins = checked(profile.fishCoins + battle.fishCoins);
                ApplyLevelUp(profile, previousExperience);
            }
            tx.Put(PlayerRecordKind.Profile, profile);
            tx.Put(PlayerRecordKind.Battles, battle);
            return battle;
        });
    }

    public Task<OperationResult<bool>> RecoverInterruptedBattlesAsync()
    {
        string completed = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return ExecuteCommandAsync(tx => {
            var profile = ProgressionProfile(tx);
            bool changed = false;
            while (true) {
                var batch = tx.Query<BattleAttempt>(new PlayerRecordQuery { Kind = PlayerRecordKind.Battles, State = (int)BattleOutcome.Running });
                if (batch.Count == 0) break;
                foreach (var battle in batch) {
                    battle.outcome = BattleOutcome.Error; battle.completedUtc = completed;
                    profile.tickets = checked(profile.tickets + 1);
                    tx.Put(PlayerRecordKind.Battles, battle); changed = true;
                }
            }
            if (changed) tx.Put(PlayerRecordKind.Profile, profile);
            return true;
        });
    }

    public Task<OperationResult<bool>> ClaimEquipmentAsync(string itemId) => ExecuteCommandAsync(tx => {
        var profile = ProgressionProfile(tx); ProgressionEquipmentAccess(tx, profile);
        var definition = EquipmentRules.Find(itemId);
        ProgressionRequire(definition != null, "Unknown equipment.");
        ProgressionRequire(!definition.IsLoot, "Available from the equipment chest in Shop.");
        ProgressionRequire(tx.Read<EquipmentItem>(PlayerRecordKind.Equipment, itemId) == null, "This component is already owned.");
        var progress = tx.Read<BossProgress>(PlayerRecordKind.BossProgress, definition.Tier.ToString(CultureInfo.InvariantCulture));
        int required = 5 * ((int)definition.Slot + 1);
        ProgressionRequire(progress != null && progress.wins >= required, "Requires " + required + " wins against this boss.");
        ProgressionRequire(progress.pendingClaims > 0, "No mastery rewards available.");
        profile.equipmentUnlocked = true; progress.pendingClaims--; progress.claimedComponents++;
        tx.Put(PlayerRecordKind.Profile, profile); tx.Put(PlayerRecordKind.BossProgress, progress);
        tx.Put(PlayerRecordKind.Equipment, new EquipmentItem { id = itemId, level = 1 });
        return true;
    });

    public Task<OperationResult<bool>> EquipAsync(string itemId, EquipmentSlot slot) => ExecuteCommandAsync(tx => {
        var profile = ProgressionProfile(tx); ProgressionEquipmentAccess(tx, profile);
        var definition = EquipmentRules.Find(itemId);
        ProgressionRequire(definition != null && definition.Slot == slot && tx.Read<EquipmentItem>(PlayerRecordKind.Equipment, itemId) != null, "This item is not owned or does not fit this slot.");
        if (EquipmentRules.Equipped(profile, slot) == itemId) return true;
        profile.equipmentUnlocked = true; EquipmentRules.SetEquipped(profile, slot, itemId);
        tx.Put(PlayerRecordKind.Profile, profile); return true;
    });

    public Task<OperationResult<bool>> UnequipAsync(EquipmentSlot slot) => ExecuteCommandAsync(tx => {
        var profile = ProgressionProfile(tx); ProgressionEquipmentAccess(tx, profile);
        ProgressionRequire(Enum.IsDefined(typeof(EquipmentSlot), slot), "Invalid equipment slot.");
        EquipmentRules.SetEquipped(profile, slot, null); tx.Put(PlayerRecordKind.Profile, profile); return true;
    });

    public Task<OperationResult<bool>> AutoEquipAsync() => ExecuteCommandAsync(tx => {
        var preview = ProgressionEquipment(tx); ProgressionEquipmentAccess(tx, preview);
        var options = Enumerable.Range(0, 3).Select(slot => preview.equipment.Where(item => (int)EquipmentRules.Find(item.id).Slot == slot)
            .Select(item => item.id).Prepend((string)null).ToArray()).ToArray();
        string[] best = Enumerable.Range(0, 3).Select(slot => EquipmentRules.Equipped(preview, (EquipmentSlot)slot)).ToArray();
        int stage = preview.masterStage > 0 && ProgressionBossGate(preview, 1 + (preview.masterStage - 1) % 3, preview.masterStage) == null ? preview.masterStage : 0;
        int bossId = stage > 0 ? 1 + (stage - 1) % 3 : ProgressionBossGate(preview, 3, 0) == null ? 3 : ProgressionBossGate(preview, 2, 0) == null ? 2 : 1;
        var target = BattleRules.Boss(bossId, stage);
        double power = EquipmentRules.AutoEquipScore(preview, target);
        foreach (var weapon in options[0]) foreach (var body in options[1]) foreach (var head in options[2]) {
            EquipmentRules.SetEquipped(preview, EquipmentSlot.Weapon, weapon);
            EquipmentRules.SetEquipped(preview, EquipmentSlot.Body, body);
            EquipmentRules.SetEquipped(preview, EquipmentSlot.Head, head);
            double candidate = EquipmentRules.AutoEquipScore(preview, target);
            if (candidate > power + 1e-9) { power = candidate; best = new[] { weapon, body, head }; }
        }
        for (int slot = 0; slot < 3; slot++) EquipmentRules.SetEquipped(preview, (EquipmentSlot)slot, best[slot]);
        tx.Put(PlayerRecordKind.Profile, preview); return true;
    });

    public Task<OperationResult<bool>> UpgradeEquipmentAsync(string itemId, int expectedLevel, string requestId) => ExecuteCommandAsync(tx => {
        ProgressionRequire(!string.IsNullOrWhiteSpace(requestId), "Missing upgrade request.");
        var receipt = tx.Read<EquipmentUpgradeReceipt>(PlayerRecordKind.EquipmentUpgrades, requestId);
        if (receipt != null) {
            ProgressionRequire(receipt.itemId == itemId && receipt.fromLevel == expectedLevel, "Upgrade request does not match.");
            return true;
        }
        var profile = ProgressionProfile(tx); ProgressionEquipmentAccess(tx, profile);
        var item = tx.Read<EquipmentItem>(PlayerRecordKind.Equipment, itemId);
        ProgressionRequire(item != null && EquipmentRules.Find(itemId) != null, "This item is not owned.");
        ProgressionRequire(item.level == expectedLevel, "Equipment changed. Review the new upgrade cost.");
        ProgressionRequire(item.level < ProgressionLevel(profile), "Reach Cat Lv. " + (item.level + 1) + " to upgrade.");
        int cost = EquipmentRules.UpgradeCost(item.level);
        ProgressionRequire(profile.fishCoins >= cost, "Not enough Fish Coins.");
        profile.fishCoins -= cost; item.level++; profile.equipmentUnlocked = true;
        tx.Put(PlayerRecordKind.Profile, profile); tx.Put(PlayerRecordKind.Equipment, item);
        tx.Put(PlayerRecordKind.EquipmentUpgrades, new EquipmentUpgradeReceipt { id = requestId, itemId = itemId, fromLevel = expectedLevel });
        return true;
    });

    public Task<OperationResult<LootReceipt>> DrawLootAsync(int count, string requestId)
        => DrawLootAsync(count, requestId, progressionLootRandom.Next);

    internal async Task<OperationResult<LootReceipt>> DrawLootAsync(int count, string requestId, Func<int, int> next)
    {
        string completed = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var result = await ExecuteCommandAsync(tx => {
            ProgressionRequire(!string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128, "Invalid chest request.");
            var prior = tx.Read<LootReceipt>(PlayerRecordKind.LootReceipts, requestId);
            if (prior != null) { ProgressionRequire(prior.count == count, "Chest request does not match."); return prior; }
            ProgressionRequire(count == 1 || count == 10, "Choose one chest or ten chests.");
            ProgressionRequire(!ProgressionBattleActive(tx), "Finish the current battle before opening a chest.");
            var profile = ProgressionProfile(tx);
            ProgressionRequire(profile.tickets >= count, "Not enough Boss Tickets. Earn more through Tasks or Focus.");
            string[] selections;
            lock (pendingLootSelections) {
                if (pendingLootSelections.TryGetValue(requestId, out selections)) ProgressionRequire(selections.Length == count, "Chest request does not match.");
                else {
                    ProgressionRequire(pendingLootSelections.Count < 16, "Retry your pending chest requests before opening another chest.");
                    selections = Enumerable.Range(0, count).Select(_ => LootRules.Pick(next).Id).ToArray();
                    pendingLootSelections.Add(requestId, selections);
                }
            }
            var receipt = new LootReceipt { id = requestId, count = count, completedUtc = completed, drops = new LootDrop[count] };
            profile.tickets -= count;
            for (int i = 0; i < selections.Length; i++) {
                var definition = EquipmentRules.Find(selections[i]);
                bool duplicate = tx.Read<EquipmentItem>(PlayerRecordKind.Equipment, definition.Id) != null;
                int fish = duplicate ? LootRules.DuplicateFish(definition.Tier) : 0;
                if (duplicate) profile.fishCoins = checked(profile.fishCoins + fish);
                else tx.Put(PlayerRecordKind.Equipment, new EquipmentItem { id = definition.Id, level = 1 });
                receipt.drops[i] = new LootDrop { itemId = definition.Id, duplicateFish = fish };
            }
            profile.equipmentUnlocked = true;
            tx.Put(PlayerRecordKind.Profile, profile); tx.Put(PlayerRecordKind.LootReceipts, receipt);
            return receipt;
        });
        if (result.Succeeded) lock (pendingLootSelections) pendingLootSelections.Remove(requestId);
        return result;
    }

    public Task<OperationResult<bool>> RenamePlayerAsync(string name)
    {
        string value = (name ?? "").Trim();
        return ExecuteCommandAsync(tx => {
            ProgressionRequire(AccountAggregate.ValidName(value), "Enter a display name of 1–20 characters without markup or control characters.");
            var profile = ProgressionProfile(tx);
            var account = AccountPreferences.Normalize(tx.Read<AccountPreferences>(PlayerRecordKind.Account, "account"));
            profile.username = value; account.playerName = value;
            tx.Put(PlayerRecordKind.Profile, profile); tx.Put(PlayerRecordKind.Account, account); return true;
        });
    }

    public Task<OperationResult<bool>> SaveSettingsAsync(AppSettings settings)
    {
        var draft = settings == null ? null : PlayerDataStore.Copy(settings);
        return ExecuteCommandAsync(tx => {
            ProgressionRequire(draft != null && !float.IsNaN(draft.soundVolume) && !float.IsInfinity(draft.soundVolume)
                && !float.IsNaN(draft.musicVolume) && !float.IsInfinity(draft.musicVolume), "Choose valid sound and music volumes.");
            tx.Put(PlayerRecordKind.Settings, AppSettings.Normalize(draft)); return true;
        });
    }

    public Task<OperationResult<bool>> SaveAccountSettingsAsync(AccountPreferences settings)
    {
        var draft = settings == null ? null : PlayerDataStore.Copy(settings);
        return ExecuteCommandAsync(tx => {
            ProgressionRequire(draft != null, "Account settings are missing.");
            var value = AccountPreferences.Normalize(draft);
            var previous = AccountPreferences.Normalize(tx.Read<AccountPreferences>(PlayerRecordKind.Account, "account"));
            ProgressionRequire(string.IsNullOrEmpty(previous.boundUid) || previous.boundUid == value.boundUid, "This local save is linked to another Google account.");
            value.playerName = (value.playerName ?? "").Trim();
            ProgressionRequire(string.IsNullOrEmpty(value.playerName) || AccountAggregate.ValidName(value.playerName), "Enter a display name of 1–20 characters without markup or control characters.");
            ProgressionRequire(!value.uploadEnabled || AccountAggregate.ValidName(value.playerName), "Choose a display name before sharing statistics.");
            tx.Put(PlayerRecordKind.Account, value); return true;
        });
    }

    // Account transport callbacks and consent controls update different fields.
    // Merge those changes against the latest committed row, rather than restoring
    // a stale whole-object draft after an asynchronous database/network wait.
    internal Task<OperationResult<bool>> PatchAccountSettingsAsync(Action<AccountPreferences> patch)
    {
        if (patch == null) throw new ArgumentNullException(nameof(patch));
        return ExecuteCommandAsync(tx => {
            var value = AccountPreferences.Normalize(tx.Read<AccountPreferences>(PlayerRecordKind.Account, "account"));
            string previousOwner = value.boundUid;
            patch(value);
            value = AccountPreferences.Normalize(value);
            ProgressionRequire(string.IsNullOrEmpty(previousOwner) || previousOwner == value.boundUid, "This local save is linked to another Google account.");
            value.playerName = (value.playerName ?? "").Trim();
            ProgressionRequire(string.IsNullOrEmpty(value.playerName) || AccountAggregate.ValidName(value.playerName), "Enter a display name of 1–20 characters without markup or control characters.");
            ProgressionRequire(!value.uploadEnabled || AccountAggregate.ValidName(value.playerName), "Choose a display name before sharing statistics.");
            tx.Put(PlayerRecordKind.Account, value); return true;
        });
    }

    public Task<OperationResult<bool>> InitializeTutorialAsync() => ExecuteCommandAsync(tx => {
        var current = tx.Read<TutorialProgress>(PlayerRecordKind.Tutorial, "tutorial");
        if (current != null && current.version == 1) return true;
        int level = ProgressionLevel(ProgressionProfile(tx));
        tx.Put(PlayerRecordKind.Tutorial, new TutorialProgress { version = 1, taskStep = 4, announcedLevel = level,
            read = new List<string> { "tasks", "reward", "daily", "equipment", level >= 2 ? "focus" : "", level >= 5 ? "boss" : "", level >= 5 ? "leaderboard" : "" } });
        return true;
    });

    public Task<OperationResult<bool>> SaveTutorialAsync(TutorialProgress progress)
    {
        var draft = progress == null ? null : PlayerDataStore.Copy(progress);
        return ExecuteCommandAsync(tx => {
            ProgressionRequire(draft != null, "Tutorial progress is missing.");
            draft.version = 1; tx.Put(PlayerRecordKind.Tutorial, draft); return true;
        });
    }
}
