# Equipment

Source: Cat Quest.pdf sections 4.4.8, 4.5.4, Tables 4.6-4.8, 4.5.5 and 4.6.4 (PDF pages 58, 62-67, 75).

- Three deterministic Boss sets, each with Weapon (ATK), Armour (DEF), Head Accessory (ASPD).
- Level 1 on acquisition. Weapon/body bonus = 2T + 0.5(E - 1); head bonus = 0.05T + 0.01(E - 1).
- Upgrade E to E+1 costs 10+5E Fish, capped by Cat Level. All changes persist atomically; upgrade request receipts make retries idempotent.
- Two-piece bonus is 5T percent of level-derived maximum HP, rounded half upward. Three pieces retain this and add T ATK, T DEF and 0.02T ASPD. Mixed sets do not combine.
- Final stats drive battle damage and actual attack intervals; confirmed attempts snapshot both final stats and equipped items/levels. Equipment changes are blocked during running battles.
- Normal Boss victories 5/10/15 each allow one unowned component. Claims grant no Fish; later wins after the full set grant the existing post-set rewards.
- Schema 5 preserves task/focus/currency/Boss progress and outstanding mastery claims, adds inventory/equipped IDs/upgrade receipts and permanent equipment unlock.
- Compatibility with the existing two independent Daily Reward cards: claiming either card unlocks equipment permanently. This implements first-Daily-Reward access without changing the established reward system.
- Cat Home shows the forest/cat preview, three slots below it (weapon, armour, helmet), coloured HP/ATK/DEF/ASPD bars with white values, and a square inventory grid. Four filters: All, Helmet, Weapon, Equipment. No capacity or selling UI. Stage armour bars open claim dialogs directly on Boss; there are no claims in Cat Home. Dashboard slots show equipped gear and link to item management.
- Equipment icons use existing GUI Pro assets. Items change combat stats; no new cat skins or crafting/enhancement tree are introduced.

Editor requests: Temp/setup-equipment.request; Temp/verify-equipment.request; Temp/verify-equipment-play.request.
Rules results: Temp/verify-equipment.result. UI results/screenshots: Library/EquipmentVerification/.
Checks use isolated saves and never alter the user's save. UI coverage includes 1920/1280, confirmations/cancel, duplicate/stale requests, failed writes, ownership/slot validation, nine items, set bonuses, level/fund restrictions, reload, Stage/Dashboard entry and ASPD animation scaling.

Visual grade: Cyclope blue, Orc purple, Demon yellow. EquipmentGradeEffect animates the original 4x4 Square_Sheet_1_Blue/Purple/Yellow textures used by Square_Effect_1/3/4, with unscaled time and no demo icon particles. Grade effects appear on backpack items, worn slots and claim choices. No material or texture allocations per frame.

Auto Select evaluates every owned loadout including set bonuses, retains the existing loadout on equal Power, then commits the best loadout atomically after confirmation. It respects the unlock and active battle gates. Stat bars are illustrative scales, not current health: HP relative to base HP x1.2, ATK/DEF relative to base +12, ASPD relative to 2/s; white text is the authoritative final value.
