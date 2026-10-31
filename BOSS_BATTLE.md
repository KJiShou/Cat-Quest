# Boss battles

Specification: `D:/Downloads/Cat Quest.pdf`, sections 4.5.4Ã¢â‚¬â€œ4.5.5 (PDF pages 62Ã¢â‚¬â€œ68). Equipment acquisition, upgrade, equip/unequip and stat modifiers are implemented; see EQUIPMENT.md.

## Rules

- Cat stats derive from saved EXP using the existing level curve: HP `100 + 10*(level-1)`, ATK/DEF `5 + level-1`, ASPD `1.00`.
- Power is display-only: `round(HP + 10*ATK + 8*DEF + 100*ASPD)`.
- Damage is `max(1, round(ATK - 0.5*DEF))`. The document does not define half ties; this implementation consistently rounds .5 away from zero.
- First hit is after one attack interval; subsequent hits occur every `1 / ASPD` seconds. Cat wins simultaneous lethal hits. The 120-second battle limit excludes the introductory run.
- BattleSimulation uses attack counters and double-precision times; frame rate and animation events cannot award damage or affect the outcome.
- Each confirmed entry atomically records immutable stats and spends one ticket. A valid defeat or explicit stop consumes it. Technical errors/timeouts and unfinished attempts found on startup refund exactly once.
- Settlement recomputes the outcome from the saved snapshot and commits reward, mastery, result and unlocks together. Failed writes can be retried without duplicate rewards.
- Normal bosses: Cyclope (90/8/5/.90), Orc (220/18/10/1.00), Demon (450/35/18/1.10), in HP/ATK/DEF/ASPD order. Orc needs a Cyclope victory and level 10; Demon needs an Orc victory and level 20.
- First victories award 30/60/100 EXP. Subsequent normal-boss victories award 10/20/30 EXP respectively, including equipment milestones. These ticket-funded battle rewards are separate from the task and Focus caps. Existing settled receipts are not paid again. Wins 5/10/15 create pending equipment claims. The three chests award Weapon at 5 wins, Body at 10 and Head at 15, directly on click. Existing owned components and pending claims are preserved. Repeat Fish rewards begin after three components have actually been claimed.
- Master unlocks after Demon. Three baseline stat sets and linear per-cycle scaling follow the PDF. Only the current stage is enterable; its first victory awards `50+10*n` EXP and `25+5*n` Fish, then advances the stage.
- Save schema 5 preserves older task, focus, currency and XP data. It adds boss mastery, pending claims, battle receipts/snapshots and the next Master stage.

## Presentation

`BossBattle1.unity` remains the authored environment source; the battle uses its generated preview prefab. The upper-left Stage button opens three hexagonal portrait cards derived from the supplied Light/Stage prefab. Stage 2 uses `OldForestBossStage.prefab`, exported from `Assets/Scenes/Old forest.unity`; Stage 3 uses `LavaDungeonBossStage.prefab`, exported from `Assets/Scenes/Lava dungeon.unity`. Each retains its authored environment, camera projection and boss placement; the prefab camera is centered horizontally on the actors, following the Stage 1 export convention. The exported cat root is named `Ninja Cat` for the existing actor bindings. BossArenaPreview switches the prefab with the selected boss, including Master stages; BossBattleActors reuses the authored Cyclope, Orc or Demon.

The cat runs into sword range before combat. An attack clip plays one cycle per attack interval; animation speed is `clip.length * ASPD`. Idle/run/death are not accelerated by ASPD. Red flashes and floating numbers indicate hits without interrupting the attack cadence. HP resets at entry. Navigation away explicitly stops combat; closing the application refunds an unfinished battle.

During battle, the upper-right Skip control opens the Yes/Later confirmation copied
from `IntroStory.unity` (`StorySkipUI`'s button and `Skip Popup`). The copy is an editable
`BattleSkip` subtree in Canvas, with a square button and centered icon. Its confirmation
is parented directly to the root Canvas so the black dimmer covers the header and
sidebar as well as the battle. The unused TV decoration (including its oval shadow)
is hidden; the story scene is unchanged. Confirmation pauses
simulation and character animators. Later, backdrop dismissal or Escape resumes;
Yes advances the existing deterministic simulation to its result without rendering
each hit, then uses the normal settlement and result UI. It does not guarantee a win,
spend another ticket or bypass save failure/retry. Leaving the page retains the normal
Stop behavior. The dialog keeps keyboard focus on its choices.

`Temp/verify-boss-skip.request` runs `BossSkipVerification` against isolated saves.
It checks confirmation/pause/resume, deterministic win and defeat, repeated input,
save failure/retry, dismissal and page closure, with screenshots and a story UI
reference under `Library/BossSkipVerification/`.

`BossPageSetup` retains only BossBattle1 stage/preview synchronization (`Refresh Boss Scene`, also triggered when the source scene is saved). The legacy Canvas UI, combat controller and portrait generation path has been retired; their saved assets remain editable and in use. Source character/monster assets and the user's scene are not modified. Monster demo animation behaviours are disabled only on instantiated combat actors.

## Verification

Editor request files:

- `Temp/verify-battle-rules.request`: combat math, frame-rate independence, gates, atomic save failures/retries, first rewards, mastery, Master scaling/rewards, recovery and old-save migration; also existing Focus/reward/task regression checks.
- `Temp/verify-battle-play.request`: isolated save, stage portraits and gate checks, 1920x1080 and 1280x720 screenshots, confirmation, ticket spend, cat run, attack/victory, doubled animation speed at 2 ASPD, and navigation stop. Results/screenshots go to `Library/BattleVerification`.

No player save is used by these checks. Equipment is implemented in Cat Home; leaderboard remains separate.

Combat impact presentation: BossBattleActors layers a 0.32s outward recoil, tilt and squash/rebound over the current animated pose for both cat and each boss, triggered only by actual simulation hits. Attack clips retain their configured ASPD and recover their phase after the visual freeze; no damage or simulation timing comes from feedback. Repeated hits restart a bounded reaction, and finish/reset/stage selection/page disable/focus loss clear additive transforms and flash.

Impact emphasis: each hit freezes only the two actor Animators for 55 ms, holds the recoil pose/flash, and shakes only the arena camera for 160 ms with a decaying offset. Simultaneous hits share one stop. On release the Animators catch up the held time to retain the attack phase; BattleSimulation and Time.timeScale are never paused by feedback. Cleanup restores animator speeds and the camera exactly, including finish, reset, page disable and focus loss.

Ordinary committed victories now reserve 1.8 seconds for the existing BossBattleActors death presentation: a 160 ms final-pose hold, 0.55-speed authored death clip, a 6% arena-camera push that returns to its original projection, one decaying camera shake and a warm tint fading into transparency. The boss stays down with zero HP until result dismissal. The existing SimpleCasual Light/Play_Result_Victory_Common-derived VictoryResult, victory sound and committed reward flights appear after this beat. Explicit Skip remains immediate. Saving happens before celebration; failed saves show Retry Save without victory effects. Navigation/disable/reset/focus loss clear actor and camera effects; leaving the page cancels the pending result. Master aura is hidden during the death and restored by the normal preview reset. No battle math, reward amounts, save schema, global time scale or authored prefab is changed.

Design references: Masahiro Sakurai's [Stop for Big Moments!](https://www.youtube.com/watch?v=OdVkEOzdCPw), [Eight Hit Stop Techniques](https://www.youtube.com/watch?v=tycbMSjDDLg), and Lin et al.'s [What Features Influence Impact Feel?](https://faculty.washington.edu/zkwen/articles/lin22features.pdf). The chosen timing/intensity is specific to Cat Quest, not a claimed value from those sources. `Temp/verify-boss-death.request` uses the existing BossRewardVerification runner with isolated saves, restoration and a bounded deadline. It checks all three bosses at 1920x1080/1280x720, captures motion frames, and exercises delayed results/flights, duplicate input, dismissal, focus loss, navigation, failed-save retry, reload and immediate Skip. Fresh evidence is under timestamped `Library/BossRewardVerification/` folders.

Stage reward chests show lightweight golden UI star particles while pendingClaims is positive. Particles stop with the Stage panel and clear immediately after the last claim. They do not intercept clicks or affect reward state. Victory presentation shows the existing Victory banner and only positive EXP from the committed receipt, using the Daily Reward EXP icon beside the amount; zero-EXP victories hide the reward strip, and mastery/next-stage text is omitted. EXP flies from the reward amount to the header experience bar using the shared RewardFlightUI only after a successful commit. Save failures produce no flight; retries and repeated input cannot pay twice.

`Temp/verify-boss-reward.request` runs isolated Boss reward UI checks at 1920x1080 and 1280x720: icon/count, moving EXP flights, header totals, skip/ordinary finish, duplicate settlement, failed save/retry, page closure, reload and defeat. Frame captures and results are written under `Library/BossRewardVerification/`.

Boss victories also show a second editable icon/count strip for positive Fish Coins from the committed receipt. Zero Fish hides the strip. The shared reward flight uses separate EXP and Fish origins and flies each currency to its corresponding header target; no reward rules are changed. BossRewardVerification covers normal post-set and Master Fish rewards, hidden zero state, flight movement/settlement, duplicate settlement and failed-save retry.

`Temp/verify-boss-stages.request` runs the isolated BossStageVerification Play Mode checks: all three environment/actor bindings, repeated switching, run/impact smoke, Master mapping, page closure/reopen and 1920x1080 / 1280x720 captures. Results are in `Library/BossStageVerification/`. These prefabs are editable assets; the one-time export helpers were removed.

Master victory keeps the completed stage visible while its result is open. Dismissing the result (or leaving and reopening the page) selects the persisted next Master stage and its environment. It does not start a battle: Challenge still opens the existing one-ticket confirmation. Defeat, Stop and unsuccessful settlement do not advance the selection. BossRewardVerification also covers dismissal, cyclic mapping, repeated close, confirmation/cancel, failed-save retry, page reopening and progression reload.

The Master selector is an editable gold challenge card under the existing StageSelection/Master object in Canvas.prefab. It reuses the project stage-card frame, the Light/Stage StagePIcture hexagon and SimpleCasual crown artwork, with the existing project font and JuicyButton feedback. Its caption, next boss description, locked hint and entry label are presenter bindings; selecting it still only previews the current Master stage. Run Temp/verify-master-card.request for isolated locked/unlocked, pointer/keyboard feedback and 1920x1080 / 1280x720 checks; evidence is written under Library/MasterCardVerification/.

Master previews use the shared Assets/Prefabs/Boss/MasterBossAura.prefab: gold-orange Cyclope, purple Orc and dark-red Demon, with a fixed body-center anchor, subtle unscaled breathing and sparse upward motes. The effect sits below actor sprite sorting orders and has no depth velocity. Normal stages hide it; stage instances own its lifetime. BossArenaPreview holds the serialized prefab binding; BossPageUI passes the selected Master state. No combat, reward or entry rules change. Temp/verify-master-aura.request runs isolated normal/Master visibility, color, stationary anchor, motion, sorting, combat/reset and page lifecycle checks at 1920x1080 and 1280x720; output is in Library/MasterAuraVerification/.

Mastery rewards use the existing EquipmentClaimUI with one row of chests (no lower choice row). Blue/purple/yellow backgrounds identify Cyclope/Orc/Demon quality. Each chest previews its fixed item; a committed claim reveals the item and Owned status. Hover, including locked and collected rewards, reuses the non-blocking EquipmentItemDetail preview with actual stats. Service-level win gates and existing atomic claim settlement prevent early or duplicate rewards. Legacy out-of-order owned equipment is retained and remaining pieces can still be collected. Temp/verify-chest-reward.request runs isolated UI/hover/claim checks; EquipmentVerification includes fixed milestone boundaries, failure/retry and legacy completion.

After a committed victory or defeat, the cat automatically runs out through the right edge while the result awaits confirmation. The existing victory death beat remains independent. Result Continue immediately plays EndTransition, resets the normal/current stage or selects the persisted next Master stage under cover, then plays StartTransition. Both are editable instances of the existing transition prefabs inside the Boss Arena mask, with serialized presenter bindings; the header/sidebar and original global transitions are untouched. Repeated Continue/Challenge/stage inputs are ignored during return; navigation cancels it and clears both wipes. Defeat returns to the same stage. Entry and settlement remain unchanged; the next Challenge still requires confirmation and one ticket. BossRewardVerification checks automatic exit before confirmation, no premature wipe, immediate closing on confirmation, local mask bindings, duplicate input and Master mapping at 1920/1280.
