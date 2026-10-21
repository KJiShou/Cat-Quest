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

Equipment bindings are already serialized in Assets/Prefabs/Canvas.prefab. The one-off page setup tool has been retired; update the affected component or prefab section without rebuilding the page.
Editor verification requests: Temp/verify-equipment.request; Temp/verify-equipment-play.request.
Rules results: Temp/verify-equipment.result. UI results/screenshots: Library/EquipmentVerification/.
Checks use isolated saves and never alter the user's save. UI coverage includes 1920/1280, confirmations/cancel, duplicate/stale requests, failed writes, ownership/slot validation, nine items, set bonuses, level/fund restrictions, reload, Stage/Dashboard entry and ASPD animation scaling.

Visual grade: Cyclope blue, Orc purple, Demon yellow. Backpack items, worn slots and claim choices use SimpleCasual rounded UI backgrounds with tier colours. Animated EquipmentGradeEffect borders are no longer generated. Upgrade hover feedback and battle weapon effects remain separate and unchanged.

Auto Select evaluates every owned loadout including set bonuses, retains the existing loadout on equal Power, then commits the best loadout atomically on click without confirmation. It respects the unlock and active battle gates. Stat bars are illustrative scales, not current health: HP relative to base HP x1.2, ATK/DEF relative to base +12, ASPD relative to 2/s; white text is the authoritative final value.

Item details use the editable project prefab Assets/Prefabs/EquipmentItemDetail.prefab, adapted from GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Equipment_Popup_Detail.prefab. EquipmentPageUI supplies the real item, level, current/next bonus, set bonuses and Fish cost; EquipmentItemDetailUI contains serialized visual bindings and confines keyboard navigation to available detail actions. Clicking the backdrop closes the detail without activating the underlying page; clicks inside the card and drags do not dismiss it. Equip/unequip execute directly and show their result in the detail panel. Upgrade executes directly from the Fish button, with failures shown in the detail status. Only Claim retains confirmation; Auto Select directly equips the strongest owned loadout; service settlement remains authoritative. The project Fredoka font is retained. The two Canvas EquipmentPageUI components reference this prefab; the Boss mastery choice panel remains separate.

Dashboard and Cat Home share Assets/Prefabs/EquipmentStats.prefab (EquipmentStatsUI), adapted from the SimpleCasual Light Equipment/Panel_Left/Stats_Group and the existing Cat Home flat-colour bars. Both show final equipped HP/ATK/DEF/ASPD plus Total Power from CombatStats.Power, including set bonuses. Dashboard slot backgrounds now use the same tier tints as Cat Home. Equipment detail, mastery choices and confirmation overlays cover the root Canvas and block header/sidebar input while retaining page lifecycle ownership. Upgrade prices use the existing header fish icon.

The Cat Home inventory surface uses the editable Assets/Prefabs/EquipmentInventory.prefab adapted from Light/Equipment.prefab Right_Panel: separate filter strip, six-column/four-row item grid with level badges, and Auto Select. Sale, capacity counters, Inventory/Equipment headings and the blue saved message are omitted. Empty/locked guidance uses neutral text; the green item dot denotes equipped gear. EquipmentInventoryUI holds serialized templates and responsive grid sizing; EquipmentPageUI remains the action/data presenter.

Both character cards display Total Power as the fist-icon numeric badge from Light/Character_Detail_2.prefab Middle_Character_Group/Platform/Power, authored in EquipmentStats.prefab. The existing text object/font and real CombatStats.Power binding are preserved; the visible Total Power label is replaced by the icon.

The Fish upgrade button previews the item's next-level bonus delta in green beside the current stat on hover or keyboard selection. EquipmentUpgradeFeedback renders the actual Upgrade_Green prefab while previewing, independently of JuicyButton motion; exit, disable, modal confirmation and focus loss clean up the effect. Previewing never upgrades or spends currency. Upgrade has no confirmation; clicking Fish directly submits the displayed level and cost to the existing service checks. Both Power badges are enlarged and the Dashboard character preview is positioned higher. The bottom TaskStatus success strip is removed; task errors use the existing dismissible dialog.

Dashboard equipment slots leave clearance above the enlarged Power badge. Dashboard and Cat Home empty slots use the vendor 512px PictoIcon_Attack/Armor/Helmet sprites with a muted tint; equipped slots restore their existing full-colour item icons.

Item details omit the separate Next level card. The current stat card contains the hover-only upgrade delta and Upgrade_Green effect; the Fish caption remains Upgrade. Set labels use the SimpleCasual blue/purple/yellow Label_Tag00 sprites. Below the stat, three progress pips and two threshold cards show actual equipped-set progress and active 2/3-piece bonuses. These cards are serialized in EquipmentItemDetail.prefab.
