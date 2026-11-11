# Equipment Shop

[English](SHOP.md) | [简体中文](SHOP.zh-CN.md)

See the Chinese companion document for the full research, presentation rationale and rules approved on 2026-11-11.

Cat Home → Shop opens the editable `Assets/Prefabs/LootShop.prefab`. One draw costs 1 Boss Ticket; ten cost 10. Each independent draw yields one item, with no pity, guarantees, discount or duplicate protection.

| Rarity | Colour | Chance | Items | Chance per item | Duplicate Fish |
| --- | --- | --- | --- | --- | --- |
| Legendary | Yellow | 1% | 6 | 0.1667% | 300 |
| Ultra Rare | Purple | 10% | 6 | 1.6667% | 25 |
| Rare | Blue | 89% | 6 | 14.8333% | 5 |

`LootRules.Catalog` contains all eighteen requested icons and slot mappings. Golden and steel gauntlets are weapons. Existing three-slot bonuses, upgrade costs and Cat Level caps apply. Six Legendary passives are implemented: Magic Crown multiplies total ASPD by 1.10 (within the existing 100 ASPD simulation limit); Heavy Armour has a 10% chance to cancel each incoming hit; Golden Gauntlets have a 20% chance to double post-defence hit damage. Golden Helm multiplies maximum HP by 1.15 (rounded half up); Short Sword ignores 20% of enemy DEF before damage rounding/minimum damage; Magic Staff doubles every fifth outgoing hit (5, 10, 15...), resetting each battle. These work only while equipped, and upgrading does not raise their percentages. New series abilities and weapon-specific animations are deferred. Chest items cannot count as Boss sets or mastery claims. The nine existing mastery items remain separate. Inventory accommodates all 27 items.

## Research

[Unity 6.3 weighted selection](https://docs.unity.com/en-us/engine/6000.3/manual/scripting/programming-math/class-random) explains partitioning the random range by weight. The implementation tests Legendary first (1/100). Only on failure does it test Ultra Rare (10/99); otherwise it selects Rare. It then makes a separate uniform selection from the chosen rarity pool. Thus overall rates remain 1%, 10%, and 89%; the conditional Ultra Rare test is approximately 10.101%, not 10%. The verifier exhaustively enumerates the independent rarity and item choices and checks short-circuiting and boundaries. A ten-pull has `1 - 0.99^10 ≈ 9.56%` chance of at least one Legendary, not a guarantee. One hundred independent pulls give approximately 63.40%.

[Blizzard's pack rules](https://news.blizzard.com/en-us/article/20852959/hearthstone-update-upcoming-card-pack-changes) demonstrate that duplicate protection and guarantees are separate mechanics requiring disclosure. This project follows the user's fixed-odds/no-pity decision. [Blizzard's mass opening flow](https://news.blizzard.com/en-us/article/23999067/introducing-mass-pack-opening-the-new-way-to-open-your-pre-purchase-bundles) offers reveal controls and a full summary; this shop supports Reveal all and ten result cards. [NN/g's animation guidance](https://www.nngroup.com/articles/animation-usability/) supports brief, purposeful effects without repeated forced waiting.

## Implementation

TaskService atomically saves tickets, inventory, duplicate Fish and request receipts. Repeated items inside one ten-pull convert after their first acquisition. Failed saves deliver no result. Confirmation cancellation spends nothing. Once committed, closing/skipping the animation cannot undo the reward. Duplicate request IDs return the original result; changing the count is rejected. Active battles block purchases.

Save version 6 adds receipts without resetting progress. Before the first successful desktop write of old data, `.before-shop.bak` preserves the original bytes alongside the rolling `.bak`. Browser storage retains `beforeShop` atomically. Old version-5 apps reject the newer save. Verification never uses live saves.

UI reuses SimpleCasual Light/Shop product cards and Light/Popup_Reward_Get's ribbon, surface and button, preserving Fredoka and JuicyButton. Art bindings are in `Assets/Prefabs/LootArt.asset`. Imported vendor resources are unchanged. PNG frame sequences avoid the supplied Spine 3.8.75/installed 4.2 incompatibility. The 1.8-second duration is a presentation choice, not a claim about the export frame rate. Trimmed frames are positioned on their original canvas to prevent size jumps. Ten-pulls show the highest rarity actually committed. Animation uses unscaled time, honors Reduce Motion and never owns rewards. Opening starts item flight at frame 28 while the remaining chest frames continue. Ten-pull flight takes 0.24 seconds per item with a 0.035-second gap (about 3.5 seconds total). Each imported frame references the largest chest-body sprite, not the first auto-sliced particle. Positioning uses the original 1200×1200 canvas, independent of packing.

The home shows only the chest and purchase controls. The information button below the chest opens the catalog, rates and duplicate values. The catalog uses full equipment rarity tags and slot icons; hover or keyboard selection opens the existing read-only equipment detail card. The top-right ticket counter reuses Home's widget with a smaller icon. Rates and duplicate values are behind the information button. Buttons use pixel multiplier 2. As the chest opens, items fly from its mouth into result slots one at a time (including all ten in a ten-pull); skipping reveals the already committed results immediately.

## Normal Boss balance

| Boss | HP | ATK | DEF | ASPD | First naked win | Full Rare gear at Cat Level |
| --- | --- | --- | --- | --- | --- | --- |
| Cyclope | 170 | 13 | 8 | 1.00 | Lv.8 | Lv.5 |
| Orc | 340 | 25 | 15 | 1.00 | Lv.20 | Lv.13 |
| Demon | 650 | 40 | 24 | 1.10 | Lv.38 | Lv.25 |

These deterministic thresholds describe the stated loadouts, not new gates. Naked players lose at initial access levels 5/10/20. Master Stage, unlock conditions, entry costs, rewards and interrupted-entry recovery remain unchanged.

## Verification

`Temp/verify-loot.request` checks sequential rarity tests and exact overall probabilities, duplicates, atomic failure/retry, request identity, funds, overflow, reload, migration backup, mastery isolation and Boss balance; it runs the existing Equipment aggregate chain once.

`Temp/verify-loot-play.request` runs an isolated Play Mode flow, saving timestamped evidence in `Library/LootShopPlayVerification/`. It covers four viewport sizes, keyboard submission, background raycast blocking, cancellation, save failure, reveals, focus/disable cleanup, reduced motion, three chest colours, imported art and expanded inventory. Inspect current screenshots/motion separately; historical captures do not prove later revisions.

Legendary combat effects use the saved equipment and stats with ability version 2. Version 1 retains its original crit/block behavior; version 0 has no hit procs. Helm HP and crown ASPD are saved in the entry stats. A stable snapshot seed keeps proc sequences independent of rendering frame rate and request IDs. Authoritative settlement replays the same simulation. Older attempts retain their original rules. Damage feedback displays mint BLOCK or a purple critical icon with purple damage above the actor health bar. Blocked hits do not trigger damage flash, recoil or hit-stop. Existing Boss two/three-piece sets remain unchanged; chest rarity alone grants no set bonus. Auto Select evaluates all owned three-slot combinations, including existing set bonuses, against the highest unlocked normal Boss or the current unlocked Master Stage. Its score is expected outgoing damage over min(120 seconds, expected survival time), divided by enemy HP. It uses +20% average gauntlet damage, 90% incoming damage for guard, actual pierced defence, and completed groups of five attacks for staff burst; it never searches proc seeds for lucky victories. Equal scores keep the current loadout. Displayed Power remains the existing raw-stat summary, so Auto Select can prefer a lower displayed Power. The score is an estimate, not a victory guarantee. Ability descriptions pair a serialized SimpleCasual icon with short text in both clicked and hover details.

`Temp/verify-legendary.request` runs six-passive, Auto Select, legacy/reload/frame-independence and settlement regressions plus the existing Equipment aggregate. `Temp/verify-legendary-play.request` captures all six detail/hover abilities at 1920x1080 and 1280x720 and real Boss critical/block feedback with movement, fade and cleanup assertions in `Library/LegendaryPlayVerification/`.

Inventory uses five fixed-size columns with a four-row baseline; additional items extend the clipped ScrollRect instead of shrinking all cells. Incomplete final rows are padded with empty squares. Task main list and equipment list use scrollSensitivity 8: the existing input module sends 6 UI units per wheel tick, so one tick moves at most 48 logical UI pixels, clamped at the ends. Dragging remains supported. Equipment keyboard selection reveals newly selected offscreen cells without snapping wheel scrolling back to the old selection. Ability text uses font size 38 in clicked and hover details.

Equipment hover previews omit upgrade text and Fish cost; the clicked detail keeps its upgrade button and cost.

The information catalog shows only each rarity's overall probability; individual equipment cards omit percentage labels.
