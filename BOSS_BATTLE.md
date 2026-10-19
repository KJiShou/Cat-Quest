# Boss battles

Specification: `D:/Downloads/Cat Quest.pdf`, sections 4.5.4–4.5.5 (PDF pages 62–68). Equipment acquisition UI and stat modifiers are deferred.

## Rules

- Cat stats derive from saved EXP using the existing level curve: HP `100 + 10*(level-1)`, ATK/DEF `5 + level-1`, ASPD `1.00`.
- Power is display-only: `round(HP + 10*ATK + 8*DEF + 100*ASPD)`.
- Damage is `max(1, round(ATK - 0.5*DEF))`. The document does not define half ties; this implementation consistently rounds .5 away from zero.
- First hit is after one attack interval; subsequent hits occur every `1 / ASPD` seconds. Cat wins simultaneous lethal hits. The 120-second battle limit excludes the introductory run.
- BattleSimulation uses attack counters and double-precision times; frame rate and animation events cannot award damage or affect the outcome.
- Each confirmed entry atomically records immutable stats and spends one ticket. A valid defeat or explicit stop consumes it. Technical errors/timeouts and unfinished attempts found on startup refund exactly once.
- Settlement recomputes the outcome from the saved snapshot and commits reward, mastery, result and unlocks together. Failed writes can be retried without duplicate rewards.
- Normal bosses: Cyclope (90/8/5/.90), Orc (220/18/10/1.00), Demon (450/35/18/1.10), in HP/ATK/DEF/ASPD order. Orc needs a Cyclope victory and level 10; Demon needs an Orc victory and level 20.
- First victories award 30/60/100 EXP. Wins 5/10/15 create pending equipment claims. No items are granted yet. Repeat Fish rewards require three actually claimed set components, so remain unavailable until equipment is implemented.
- Master unlocks after Demon. Three baseline stat sets and linear per-cycle scaling follow the PDF. Only the current stage is enterable; its first victory awards `50+10*n` EXP and `25+5*n` Fish, then advances the stage.
- Save schema 4 preserves older task, focus, currency and XP data. It adds boss mastery, pending claims, battle receipts/snapshots and the next Master stage.

## Presentation

`BossBattle1.unity` remains the authored environment source; the battle uses its generated preview prefab. The upper-left Stage button opens three hexagonal portrait cards derived from the supplied Light/Stage prefab. Orc and Demon currently share the forest as a temporary battle backdrop; their dedicated environments have not been authored.

The cat runs into sword range before combat. An attack clip plays one cycle per attack interval; animation speed is `clip.length * ASPD`. Idle/run/death are not accelerated by ASPD. Red flashes and floating numbers indicate hits without interrupting the attack cadence. HP resets at entry. Navigation away explicitly stops combat; closing the application refunds an unfinished battle.

`BossPageSetup` rebuilds the Canvas UI, generated combat controllers and portrait assets. Source character/monster assets and the user's scene are not modified. Monster demo animation behaviours are disabled only on instantiated combat actors.

## Verification

Editor request files:

- `Temp/verify-battle-rules.request`: combat math, frame-rate independence, gates, atomic save failures/retries, first rewards, mastery, Master scaling/rewards, recovery and old-save migration; also existing Focus/reward/task regression checks.
- `Temp/verify-battle-play.request`: isolated save, stage portraits and gate checks, 1920x1080 and 1280x720 screenshots, confirmation, ticket spend, cat run, attack/victory, doubled animation speed at 2 ASPD, and navigation stop. Results/screenshots go to `Library/BattleVerification`.

No player save is used by these checks. No equipment or leaderboard UI is implemented by this change.
