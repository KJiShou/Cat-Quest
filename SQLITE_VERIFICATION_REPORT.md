# SQLite migration verification — 2026-11-16

Status: SQLite implementation and the follow-up Equipment/Battle fixes are verified within the scopes below. The actual old 1.2.1 executable/update route still requires an isolated Windows environment. The complete 29-command JSON/SQLite performance comparison passed. WebGL local experience passed, but its Development upload-package size gate failed. All runs below use isolated saves; no live player data was used. After receiving these results, the maintainer explicitly authorized committing, pushing and publishing v1.4.0 on 2026-11-16. This is a publication decision with disclosed limitations, not a claim that every original release gate passed.

## Implemented

- Windows uses the pinned Unity sqlite-net 1.3.3 provider (resolved commit in Packages), SQLite 3.53.4, WAL, FULL synchronization, foreign keys, a serialized background executor and per-save process ownership.
- Explicit asynchronous commands replace daily full `PlayerData` copy/save. Per-record schema, transaction receipts, scoped query projections, stable history pagination and schema upgrades preserve existing reward rules.
- Old JSON migration preserves the source, validates full-field equivalence and database integrity, records recoverable migration stages and refuses silent rollback to old JSON after migration.
- Portable backups export only allowed data into a clean database. Import preserves the target account association, backs up existing progress and commits transactionally. Local recovery requires explicit selection; ordinary backups use SQLite's backup API.
- UI callers await committed results. Shutdown and updates drain accepted writes. Companion remains a client. Update packaging protects storage and includes the native DLL, license notices and long-path helper configuration.

## Executed evidence

Unless explicitly absolute, evidence paths below are relative to the project root; they are ignored test artifacts, not authored assets.

| Check | Actual result and evidence |
| --- | --- |
| Repository, migration, schema upgrade, business commands, faults and backup | PASS, 636 checks, fresh follow-up at 06:14 UTC; `Temp/SQLiteMigrationVerification/20261116-061313-repository/`. Includes cache owner/async-order regressions, batched History/Focus metadata validation, 18 tamper cases, 24 Focus SQL aggregation comparisons and 16 asynchronous browse equivalence/ownership checks. |
| Additional safety checks | PASS; `Temp/SQLiteMigrationVerification/20261116-031531-safety/` |
| Existing equipment → battle → Focus → rewards → task/CharacterHome aggregate | PASS, fresh follow-up; `Temp/SQLiteMigrationVerification/20261116-061437-followup-domain/` |
| AI/account/settings/protocol and prefab bindings | PASS; `Library/AiVerification/Data-20261116-044041/result.txt` |
| Task tabs, scoped refresh, keyboard/feedback and four resolutions | PASS; `Temp/SQLiteMigrationVerification/20261116-045240-task-ui/`. History/calendar preserve visible objects, scroll and selection for unrelated writes and update on relevant edits. Waiting for resize before the next sidebar interaction resolves the verifier's transient narrow capture; current 1280 collapsed render inspected at full width without product-layout edits. |
| Focus statistics, boundaries and four-resolution UI | PASS; `Temp/SQLiteMigrationVerification/20261116-040656-focus-statistics/`. Includes SQL aggregation UI and object/scroll preservation for unrelated writes, while tag/Focus changes update the graph. |
| Full Focus UI, three modes, caps, End Early, cancellation, retry and feedback | PASS; `Temp/SQLiteMigrationVerification/20261116-041601-focus-ui/`. Includes 1920/1280 pointer/keyboard routes, exact current reward amounts, failed settlement and retry, persisted receipts and Lv.30 previews. |
| Tutorial rules, persistence, gates and UI | PASS; `Temp/SQLiteMigrationVerification/20261116-041944-tutorial-ui/`. Current 1920×1080 Focus spotlight and 1280×720 Boss mastery renders inspected. |
| Shared button and popup feedback | PASS; `Temp/SQLiteMigrationVerification/20261116-042723-buttons/`: 796 buttons, 9,975 graphic-state checks. Current 1920 task editor and 1280 tag settings renders inspected. |
| Loot shop, single/ten pull, retry and duplicate inputs | PASS; `Temp/SQLiteMigrationVerification/20261116-042842-loot/`. Four sizes, reveal/skip, cleanup, reload, 27-item inventory and equipment abilities; current 1920 shop and 1280 results inspected. |
| Level-up and daily reward presentation | PASS; `Temp/SQLiteMigrationVerification/20261116-042919-level-up/`. Persisted rewards, queued/dismissed celebrations, four viewport sizes, keyboard, unscaled motion and focus/disable reset. |
| Calendar, visible range, task actions and feedback at four resolutions | PASS; `Temp/SQLiteMigrationVerification/20261116-034215-calendar/`. Current 1920×1080 and 1280×720 ordinary renders were inspected. |
| Settings, tags, account configuration errors, async persistence and Dashboard visibility combinations | PASS; `Temp/SQLiteMigrationVerification/20261116-035602-settings-ui/`. Both ordinary sizes and the actual vendor Settings reference were inspected. A TMP feedback buffer overrun exposed by long tags was fixed; full flow then passed. |
| AI settings, chat cancellation, draft confirmation and page lifecycle | PASS; `Temp/SQLiteMigrationVerification/20261116-040010-ai-ui/`. Current 1280 chat and 1920 draft-review renders inspected; network is a test fixture, not a live account. |
| Equipment UI | PASS, full rerun at 06:08 UTC; `Temp/SQLiteMigrationVerification/20261116-060908-equipment-passed/`. Fixed only Dashboard Stats text rectangle heights and PowerBadge vertical offset. Preserved the user's authored five-column inventory; verifier now compares against the authored prefab instead of requiring obsolete six-column geometry. All four resolutions and sidebar states, equip/claim/upgrade, retry, modal raycasts, drag/keyboard and motion checks passed. The result's old “six-column” descriptive text was corrected after this run; its executed assertion already validated authored columns. |
| Battle UI and feedback | PASS, full unobscured rerun at 06:12 UTC; `Temp/SQLiteMigrationVerification/20261116-061301-battle-final/`. Camera motion is sampled at actual render callbacks, including URP, rather than delayed Editor polling around PNG capture. All three bosses produced 59–65 rendered samples and approximately 0.045 peak camera displacement. Recoil, hit stop, settle/reset, repeated hits, victory/defeat, keyboard/pointer cleanup and 1920/1280 renders passed. Product actor/animation/layout code was not changed. |
| Boss rewards and Master progression | PASS; `Temp/SQLiteMigrationVerification/20261116-044021-boss-rewards/`. Normal/Master currencies, duplicate settlement, save failure/retry, navigation, stop/defeat and progression reload. Some reward captures are obscured by a genuine level-up popup and are not Boss visual evidence; the Battle flow's 1280 victory render was inspected separately. |
| Standalone updater | PASS, 48 checks; `Temp/SQLiteMigrationVerification/updater-20261116-031422/cases/result.txt` |
| Windows Development build, native provider | PASS, zero build errors; `Builds/SQLiteMigrationVerification/20261116-045323/隔离 Windows/`. Original ProjectSettings restored. |
| Real process crash and retry/restart | PASS; `Temp/SQLiteMigrationVerification/process-crashes-20261116-050544650-930d4b65/result.json` |
| Actual updater, new Unity migration, rewarded operation and restart | PASS; `Temp/SQLiteMigrationVerification/update-handoff-20261116-051033362-197c986f/result.json` |
| Real Focus and Companion control/reconnect | PASS; `Temp/SQLiteMigrationVerification/native-flows-20261116-050641896-a2e4c05d/result.json` |
| Actual Companion window interaction | PASS within recorded limits; same folder `window-review-result.json`, `window-initial.png`, `window-minimum.png` and state JSONs. Observed 420×300/240×180, pin toggle, actual minimum-size drag, minimized-window detection/restore and close-display while connection remains alive without a player store. Tool cannot drag outside screenshot bounds to expand; normal size was inspected before shrinking. |
| Two real main processes and shutdown timeout | PASS; `Temp/SQLiteMigrationVerification/lifecycle-20261116-062601/result.json`. Second process rejected shared-save writes. A held transaction caused the real ten-second quit timeout, with 602 rendered frames before notice acknowledgement; accepted write survived, writes resumed, and retry exited cleanly. Native retry notice was visually inspected and acknowledged. Earlier hidden-launch runs timed out awaiting an invisible native dialog; the interactive owner now launches visibly. No production shutdown behavior was changed for this result. |
| Follow-up Windows Development builds | PASS, zero errors. The 07:11 UTC build ran the complete 29-operation performance matrix (`Temp/sqlite-followup-build-path.txt`). The final 09:20 UTC build includes the duplicate-using cleanup (`Temp/sqlite-followup-final-build-path.txt`); `Temp/SQLiteMigrationVerification/20261116-091844-followup-final/windows-build.result` records its exact path. Both use an isolated verification product name. |
| Asset/reference scan | 28,895 files scanned; nine pre-existing missing-script references in four unchanged vendor/demo assets and three existing class/file-name warnings. `Temp/sqlite-followup-asset-check.json` and `Temp/sqlite-followup-preexisting-asset-findings.json`. These are not counted as clean assets or SQLite regressions. |
| WebGL local session experience | PASS within demo scope; `Temp/SQLiteMigrationVerification/20261116-0702-web-experience/result.json`. Actual browser created a username and task, settled a medium task for 25 EXP / 10 Fish Coins / 1 ticket, and claimed the first daily goal for totals of 35 EXP / 15 coins / 1 ticket. Ordinary Dashboard renders were inspected at 1280×720 and 1920×1080; no captured console warnings/errors. Reload returned to an empty username/new session. Browser tab, viewport override and localhost server were cleaned up. |
| WebGL development packaging | Unity compiled and linked the browser player; the existing itch.io packaging check FAILED because the Development `.data` file is 236,516,017 bytes, exceeding its 200 MiB single-file limit. `build-and-package-result.txt` in the Web experience evidence retains this failure. No limit was weakened and no Web package was published. The local browser check does not establish upload readiness. |

The update test uses a disposable old parent process and a JSON fixture produced by retained legacy TaskService code, **not an actual old Unity executable**. The updater and new Unity executable are real. The crash test kills processes before BEGIN, after writes before COMMIT, and after COMMIT before notification; it does not establish behavior at every instruction inside COMMIT or under hardware power loss.

At 06:38 UTC Unity reported unsaved `CharacterHome` changes. A copy was retained in `Temp/SQLiteMigrationVerification/20261116-063820-unsaved-scene/`; the user explicitly chose to save the current scene before continuing. The saved authored delta is two font-size overrides (`Stage` 21 and `Challenge` 24), with no additional layout delta. The full Battle Play flow passed again at 06:43 UTC in `Temp/SQLiteMigrationVerification/20261116-064322-battle-after-manual-save/`, and its 1280 ordinary arena and 1920 stage-selector renders were inspected. These overrides are preserved as user-approved work.

Read-only IL inspection of the available 1.2.1 release (`Assembly-CSharp.dll` SHA256 `3117962145F56F395F9F52F22CD591D0A97C26BA53DB9592022A3723FEA71C1E`) confirmed its smoke save-path override, but its local update fixture entry throws "Update fixtures are unavailable in release builds" and its Companion settings use shared PlayerPrefs. That executable was not launched for this test: full process isolation and the actual old-release update route remain unverified.

## Performance evidence

The final follow-up uses the same Windows Development executable (`20261116-071041`), machine, fixed clock and byte-identical synthetic source files for both engines. All six input SHA256 comparisons passed. It covers 29 operations × 100/1,000 active tasks × 1k/10k/100k mixed history records, with five warmups and 100 measured samples per case: **174 cases and 17,400 measured commands per engine**, 34,800 measurements total. Every raw CSV was checked for exactly samples 1–100 and allocation-total consistency. Both native runs completed successfully (SQLite 07:34:50 UTC; JSON 09:17:57 UTC).

- SQLite evidence: `C:\Users\jisho\.codex\visualizations\2026\11\16\01a1e1ea-194a-7ab3-ac2d-3d0c27f427c1\Temp\SQLiteMigrationVerification\business-SQLite-20261116-071224\sqlite-performance-20261116-071237`.
- JSON evidence: `C:\Users\jisho\.codex\visualizations\2026\11\16\01a1e1ea-194a-7ab3-ac2d-3d0c27f427c1\Temp\SQLiteMigrationVerification\business-JSON-20261116-073515\sqlite-performance-20261116-073519`.
- Combined validated index, six source hashes, full summaries and largest-case CSV: `Temp/SQLiteMigrationVerification/20261116-091817-business-comparison/`.
- Across all 174 SQLite cases, main-thread P95 was **0.0429–0.3236 ms** and end-to-end P95 **33.1296–50.6279 ms**; all unchanged 5 ms / 200 ms gates passed. End-to-end P50 was 32.6545–33.0005 ms. The largest individual sample was **413.2702 ms**; P95 acceptance is not an every-command latency guarantee.
- All 58 fixed-active 10k→100k growth and SQL-count checks passed. P95 ratios were **0.6628–1.0396**. Recorded SQL counts were 17–47 statements per measured command and WAL snapshots approximately 4.01–4.07 MiB. Every case records FULL/WAL/FK and query plans.
- In the largest 1,000-active/100,000-history case, JSON P95 was **866.3307–1225.9400 ms**, SQLite **33.1821–36.5616 ms**: approximately **24.8–36.3×** lower end-to-end P95. JSON work ran synchronously on the main thread; SQLite main-thread P95 for this case was at most 0.2544 ms.
- Allocation scope is calibrated whole-command-window allocation across all threads, including unrelated work during awaits, not pure main-thread allocation. In the largest case, mean recorded allocation per command was approximately 58 KB–1.28 MB for SQLite versus 167–173 MB for JSON. These are allocation totals, not retained heap size. Small task projections still scale with active task count; no claim of zero allocation is made.
- The earlier SaveTag/EditTask-only timing run remains retained as prior evidence; this full calibrated matrix supersedes it for final command-performance acceptance.

Largest-case per-operation comparison:

| Operation | JSON P95 ms | SQLite P95 ms | SQLite main P95 ms |
| --- | ---: | ---: | ---: |
| SaveTag | 1042.6755 | 33.9130 | 0.1116 |
| EditTask | 1212.1806 | 33.9620 | 0.1603 |
| CreateTask | 1070.4735 | 33.4790 | 0.2544 |
| DeleteTask | 866.3307 | 33.2349 | 0.1473 |
| CompleteTask | 907.1019 | 36.5616 | 0.1340 |
| UndoTask | 1068.1400 | 33.8416 | 0.1456 |
| RecompleteTask | 1059.1353 | 33.3460 | 0.1442 |
| Subtask | 1092.2260 | 33.6748 | 0.2061 |
| DeleteTag | 1225.9400 | 33.8168 | 0.0952 |
| ConfirmAiDraft | 1197.6169 | 33.4575 | 0.1463 |
| BeginFocus | 905.8652 | 33.6653 | 0.0676 |
| FinishFocus | 951.5808 | 33.1821 | 0.0611 |
| EndEarly | 1062.0624 | 34.0178 | 0.0915 |
| CancelFocus | 1170.6497 | 33.2035 | 0.0685 |
| DailyGoal | 1073.3280 | 33.2575 | 0.0877 |
| ClaimEquipment | 1009.0928 | 33.5364 | 0.0651 |
| Equip | 993.7964 | 33.1927 | 0.0896 |
| Unequip | 921.8244 | 33.7926 | 0.0776 |
| AutoEquip | 1145.5441 | 33.5129 | 0.0817 |
| Upgrade | 1067.5843 | 33.3860 | 0.0630 |
| Loot1 | 1031.4022 | 33.2064 | 0.0681 |
| Loot10 | 1035.5249 | 33.6029 | 0.0943 |
| BeginBattle | 1052.7697 | 34.8753 | 0.1734 |
| ResolveBattle | 876.1104 | 33.5450 | 0.0999 |
| RecoverBattle | 896.0551 | 33.4331 | 0.0854 |
| Settings | 909.9907 | 33.6901 | 0.0889 |
| Account | 899.2021 | 33.2971 | 0.0572 |
| Tutorial | 1031.0829 | 33.2028 | 0.0685 |
| Rename | 1213.7396 | 33.4005 | 0.0645 |


Follow-up measurement found that `GC.GetAllocatedBytesForCurrentThread` returns zero in the tested Unity runtime. The old zero allocation fields are **unavailable measurements, not evidence of zero allocation**. The first 27-operation run was deliberately interrupted after identifying this issue (`business-SQLite-20261116-062654`); a subsequent calibration also correctly failed for the inappropriate `GC.Alloc` sample-value approach. The benchmark now uses the Unity `GC Allocated In Frame` byte counter across the command window, including all threads and unrelated work during awaits, with explicit synchronous and cross-frame known-allocation calibration. It excludes setup before that window and records this scope in the report. The two complete native runs above passed both allocation calibrations and replace those invalid measurements for acceptance.

An actual visible Tasks smoke at 1k history / 100 active tasks passed lock timeout, failed-write feedback, retry and restart, with main-thread P95 2.9032 ms and end-to-end P95 37.2879 ms: `Temp/SQLiteMigrationVerification/native-20261116-024008060-18ef3100/`. This earlier small-data run is superseded by the large-data rendered result below.

The 04:41 Windows build exposed a real large-list performance failure at 1,000 active tasks / 100,000 mixed history records (50,000 task receipts plus Focus history), 1920×1080: command main-thread P95 10.9891 ms, storage P95 3.1906 ms, end-to-end P95 44.1031 ms. Evidence: `Temp/SQLiteMigrationVerification/native-20261116-044220963-84979372/`. It failed the unchanged 5 ms gate before lock/restart checks. Task browse copying/filtering/counts were subsequently moved off the UI thread with revision/generation guards; the smoke now explicitly includes asynchronous presentation work in its main-thread gate. The subsequent build/run validating that fix is recorded below.

**The subsequent 04:53 build passed this same large-data visible case:** `Temp/SQLiteMigrationVerification/native-20261116-045408629-05f58933/`. Across 100 samples, combined command + asynchronous presentation main-thread P95 was **1.5333 ms**, and end-to-end including rendered-list update P95 was **83.6944 ms** (P50 83.3264, maximum 84.2729). Following-frame gap P95 was 16.9442 ms. The same run passed 120 checks including real lock timeout, continued rendering during the three-second lock, unchanged data on failure, exactly-once retry, portable backup and a fresh process restart that ignored changed old JSON. Current 1920 failure feedback was inspected. Measurements are for SaveTag with the real visible Tasks page, not a claim about every business command.

## Limits and remaining gates

- Equipment and Battle visual failures are resolved with fresh complete runs above. The prior failed evidence remains retained. Targeted Canvas changes are recorded in `Temp/SQLiteMigrationVerification/20261116-054955-followup-baseline/Canvas.targeted.diff`; manual layout/font work was preserved.
- Final native performance, large-data visible UI, real Companion window controls, process restart, normal close and actual updater/new-player handoff passed. The actual old release executable route remains unverified for the isolation reasons above.
- The complete calibrated follow-up covers SaveTag/EditTask plus 27 additional business operations, with 100 samples in every case for both engines. Native shutdown timeout UI and simultaneous second-main-process startup passed separately as recorded above.
- WebGL passed the local in-memory experience smoke, not persistent storage or publication acceptance. Its Development package exceeds the current upload-file limit. The long synthetic username also wrapped outside the intro nameplate at 1280; that unrelated intro layout was not changed. No claim is made that every Web UI state was verified.
- The user confirmed no isolated Windows test environment is available. `SQLITE_REAL_UPGRADE_VERIFICATION.md` records the exact remaining actual-1.2.1 route and isolation requirements; no real old release was launched for verification under the current user account.
- Fault injection is not a physical full-volume/ACL test. Real power interruption was not tested.
- Exact Unicode search and undated sorting use compact metadata off the main thread. Focus statistics aggregate in SQL using .NET-generated DST boundaries; only the first relevant session per displayed tag is read for legacy tag names.
- Earlier failures are retained in timestamped evidence. Duplicate large build binaries from four failed updater attempts were removed to recover disk space; each affected folder retains `removed-duplicate-build-files.json`, logs and data. The passing updater artifacts remain intact.
- Untested checks must not be inferred as passing from the old JSON verifiers or historical screenshots.

## Final workspace checks

`Temp/SQLiteMigrationVerification/20261116-091844-followup-final/` contains the fresh static result, current Editor compilation log, serialized diff, scene diff and restored Editor state. `git diff --check` passed; 14,823 asset GUIDs contain zero duplicates; every new asset has its `.meta`; ProjectSettings are unchanged. The Canvas diff exactly matches the 12 Stats height overrides plus PowerBadge offset documented in the follow-up baseline. The pre-existing CJK font, task context menu and result-notice diffs are byte-for-byte preserved. The two user-approved CharacterHome font overrides are retained.

Unity is out of Play Mode, with the original injected `Library/ManualTestAccount/save.json`, empty Play Mode start scene and Game View size index 0 restored. The final source cleanup removed only a duplicate `using System.Collections.Generic` from AccountController; actual Unity compilation completed without C# errors. The remaining LootShopUI inherited-member and vendor Rigidbody deprecation warnings are pre-existing. No behavioral changes followed the benchmark build. The final Windows build passed at 09:20:25 UTC with zero errors. Its result, restored Editor state and 09:21 UTC cleanup check (zero verification players and zero Web test listeners) are recorded in this folder. No commit or release was created.

Automatic approval review rejected deletion of one superseded verification build directory with the reason `blocked by policy`. That deletion was not executed; no alternative deletion route was attempted. New large artifacts were placed in the C: verification workspace instead.

## Authorized v1.4.0 release preparation

After the maintainer requested commit, push, a new release and refreshed README images, the production Windows x64 build passed on 2026-11-16 at 17:31:23 Asia/Kuala_Lumpur (09:31:23 UTC), with `development=False`, unchanged `Cat Quest` product identity and zero errors. Only `bundleVersion` changed in ProjectSettings, from 1.3.1 to 1.4.0. The production executable was not launched against the host's real player profile; the native functional/performance results above remain Development-build evidence.

The release ZIP is 267,269,334 bytes, SHA-256 `750f03e878526a000903bf977b09ef305de774b552c7fe24453aff897f115231`. All 211 inventoried file sizes/hashes were verified, including the x64 SQLite library, updater config and license notices. No player database or recovery scene is included. Evidence pointers are `Temp/release-1.4.0-build-directory.txt`, `Temp/release-1.4.0-package-directory.txt` and `Temp/release-1.4.0-package-check.json`.

Eight new README screenshots passed capture/restoration at 17:33 Asia/Kuala_Lumpur using an isolated synthetic SQLite save; all were visually inspected. Raw evidence is `Temp/ReadmeReleaseCapture/20261116-093203/`. The first capture failed to locate the inactive calendar component; only the temporary lookup was corrected, and full capture passed. No product code or authored layout changed for that correction. The temporary script and `.meta` were removed. Existing recovery scene copies remain local and are now ignored by Git.
