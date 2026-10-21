# Cat Quest project review — 2026-10-20

## Decisions and scope

The user approved project rules, cleanup of confirmed unused project-owned files, and small verification-tool fixes. Existing gameplay, authored UI, player saves, third-party packages and Unity settings stay unchanged. End Early continues to settle earned Focus time under the existing caps and starts Rest after successful persistence; Cancel and invalid interruptions grant no rewards.

`AGENTS.md` is the project workflow/testing authority. `.impeccable.md` now separates global SimpleCasual design requirements from Companion-only layout requirements. Review and implementation used Unity 6000.3.10f1 with the existing custom Editor verification suite; no new framework or assembly definition was introduced.

## Completed cleanup

| Removed asset and matching `.meta` | Evidence |
| --- | --- |
| `Assets/Scripts/UI/UISpriteAnimation.cs` | No external class/path/GUID references or automatic registration. |
| `Assets/Generated/Previews/DashboardLakeIsland.png` | No name/path/GUID references. Other preview images are still used and remain. |
| `Assets/Scripts/UI/CompanionHeaderIcon.cs` | Only referenced by unused private `Icon`/`WindowButton` helpers in `CompanionWindowDesign`; those helpers were removed together. |
| `Assets/Editor/EquipmentPageSetup.cs` | One-time generator with no external callers/GUID references. Canvas already contains CatHome/Boss equipment bindings, three Dashboard slots, mastery chest/icon references and weapon slash prefabs. |
| `Assets/Editor/StatusUserSetup.cs` | One-time generator with no external callers/GUID references. Canvas already contains UserProfile and all four `CatStatusUI` references. |

References were checked again before deletion, including code and serialized GUIDs. `EQUIPMENT.md` no longer directs users to the retired setup request. No prefab, scene or existing retained `.meta` was regenerated.

The existing `Canvas.prefab` SHA256 before cleanup was `EA9A29FDC2B742B5D63466924B383CFA18D4FF06697FEF17693EC3C703ED5FCE`.

## Verification fixes

- `CharacterHomeVerification` now propagates opening/validation failures after recording them. Its internal `RunForScene(Scene)` boundary allows malformed temporary scene checks without modifying saved scenes.
- `RewardSystemVerification` retains its public `Run()` entrypoint and adds an internal `Run(Action)` boundary for testing aggregate failure propagation. A failed child cannot return normally and produce a false `ALL PASS`.
- `BossPageVerification` writes its captured arena image to `Library/BossPreviewVerification/ForestBossArena.png`, leaving the authored image under `Assets` unchanged.
- `TaskTabsVerification` no longer contains the obsolete `setup-task-tabs.request` branch that rewrote `Canvas.prefab`.
- `DashboardRewardPlayVerification` returns through the current sidebar and asserts the destination, instead of clicking the intentionally hidden old Focus return button.
- A fresh Dashboard run exposed another stale assertion: it required horizontal hover growth, while `JuicyButton` deliberately clamps horizontal scaling on wide masked rows. The verifier now checks vertical lift/stretch, bounded motion, horizontal viewport containment and reset after pointer exit/deselection. No runtime animation was changed.

All interface additions are internal Editor verification boundaries. Runtime APIs, save format, rewards and production UI behaviour are unchanged.

## Retained tools and follow-up findings

These are scoped review findings, not permission to rewrite existing pages:

| Finding | Required treatment |
| --- | --- |
| Legacy whole-page generators delete existing UI children or overwrite a prefab from a fresh root. | Do not use `DashboardRewardSetup`, `FocusPageSetup.ConfigurePage`, `CompanionPageSetup.Build`, `CompanionWindowDesign.Build` or `BossPageSetup.Build` for local UI edits. Patch the explicit component/properties instead. |
| Some setup files contain ongoing functions. | Retain Boss scene-save synchronization, Windows build support, animation baking and verification. Extract useful responsibilities before retiring mixed tools in future work. |
| Equipment static UI and dialogs are built in `EquipmentPageUI.Awake()`. | On a future change to that static portion, persist the affected component in an editable project prefab. Dynamic inventory can use reusable templates. This cleanup does not migrate/rebuild the page. |
| Sharing sprites has not ensured component-level SimpleCasual consistency. | Equipment's generic popup and literal `x` close button differ from the source popup pattern. Compare the exact light Equipment/popup source components when that UI is next changed; do not replace project fonts globally. |
| Button geometry verification does not establish style or motion quality, and can overlook controls missing feedback. | Require fresh rendered reference comparisons, motion inspection and state coverage for changed buttons/toggles/sliders. Reuse existing feedback owners. |
| Fresh Task Tabs screenshots exposed responsive layout defects despite its earlier narrow geometry assertions passing. | The subsequent user-authorized component fix below addresses the clipped Pet Widget at 1024x768 expanded and the covered task tabs at 2560x1080 collapsed. The original cleanup did not change production layout. |
| Companion message validation lacks a dedicated automated negative-command suite. | When changing native Companion or its protocol, cover stale/duplicate/wrong-owner requests in addition to Windows smoke checks. |
| Legacy Play runners lack complete timeout/environment/Game View restoration. | Bound and supervise runs, preserve/restore Editor state externally, and keep saves isolated. Future runner work should centralize lifecycle cleanup; do not launch concurrent flows. |

Keep `TaskUIBuilder`, `JuicyButtonsInstaller`, `SpriteSkinShutdown`, `StoryStepDrawer`, `IFocusPresentation` and `FocusBuildSmoke`: runtime callers, automatic registration or Editor discovery make them active despite some having few direct references. Keep source scenes, Resources, Spine examples and third-party content; this review did not establish a complete dependency graph for deleting them.

## Current acceptance evidence

Fresh acceptance results are recorded under `Library/CleanupVerification/`; existing screenshots/results elsewhere are historical unless explicitly associated with this run. Generated evidence is ignored by Git.

- Unity loaded the temporary acceptance probe after recompiling the changed Editor/runtime assemblies; no C# compilation failure was observed.
- `integrity.txt`: 24 project prefabs and 3 enabled scenes, 2,895 objects checked; no Missing Scripts or broken serialized MonoBehaviour object references.
- `failure.txt`: an empty temporary preview scene made the real CharacterHome child and Reward aggregate report FAIL and throw. Expected failure details are retained as `expected-child-failure.txt` and `expected-aggregate-failure.txt`; no saved scene was damaged.
- `equipment.txt`: normal `EquipmentVerification` and downstream Battle, Focus, Reward, Task and CharacterHome checks passed after the failure probe.
- `dashboard.txt`: Dashboard Play flow passed at 1280x720/1920x1080, including row hover/reset, separate claims, Focus quick start, sidebar return, Rest/cancel, level/HUD and reload checks. Captures: `Library/DashboardRewardPlayVerification/9fdeaaf44e6f4471b4404d40417d51ad/`.
- `boss.txt`: Boss Play flow passed at 1920x1080/1280x720, including animated preview, leaving/reopening the page and unchanged ticket balance. Capture now exists under `Library/BossPreviewVerification/`; the authored `ForestBossArena.png` SHA256 stayed `0EA901EAA9CD37EFD1EEEF09EFAC25D4F181C6D9AB7D63B815E0542913EB10DB`.
- `tabs.txt`: Task Tabs automatic checks passed at 1920x1080, 1280x720, 1024x768 and 2560x1080 with both sidebar states. **This is not a full visual pass:** image inspection found the two layout defects recorded above. Evidence: `Library/TaskTabsVerification/tabs-1024x768-expanded.png` and `tabs-2560x1080-collapsed.png`.
- Fresh Dashboard normal/hover, Boss and narrow/ultrawide Task Tabs captures were opened and inspected. No product UI was edited; this run does not certify every page against the design reference.
- `restore.txt`: restored the prior Play Mode start scene, test-store environment and Game View selected size/custom-size count. Final Editor state was stopped on CharacterHome with no dirty scene. The one-time `CleanupVerificationProbe.cs` and its `.meta` were removed after verification.
- `final-compilation.txt`: Unity recompiled the final Editor assembly after removing the temporary probe; the subsequent state check confirmed Play Mode stopped and compilation finished. Final `git diff --check` passed, Canvas retained its original hash, and no prefab/scene/asset/controller/animation, package or ProjectSettings content changed.

No Windows build is required for these Editor-only fixes and removal of unreferenced scripts/assets; Windows build/native Companion behaviour is not verified by this task. No claim of a full design-language audit of every existing page is made.

## Follow-up: responsive header and task content

The user separately authorized fixes for the two visual defects, preserving existing UI adjustments. Production changes are confined to layout components in `Assets/Prefabs/Canvas.prefab`; no runtime script, source scene or vendor component was changed.

- Header: reduced only `UserProfileGap.minWidth` from 180 to 40 and the two currency cards' `minWidth` from 347 to 300. Preferred widths, the Pet Widget itself, user portrait, fonts, artwork, scale, padding and spacing remain authored as before. The header can now shrink within the 1024x768 expanded-sidebar viewport.
- Tasks: enabled child height control through `TodayQuest`, `Content` and `TaskList`, with height expansion on the first two. Added a fixed-height LayoutElement for the existing 100-unit Title and a flexible LayoutElement for the existing list scroll viewport. Existing preferred panel heights remain intact.
- Task details: added only nested Canvas overrides for height control, a flexible scroll viewport and an unconstrained footer vertical fitter. This keeps long content scrollable and footer actions within the available height. The reusable source `TaskDetails.prefab` and its content sizing are unchanged.
- Unity prefab APIs applied the patch without rebuilding any hierarchy. Both applications produced the same asset hash. A separate serialized audit confirmed all 3,596 existing fileIDs, all object names/references and all 973 existing nested overrides were retained; only two LayoutElements and five intended details overrides were added. Two newly generated blank `m_Name` lines were whitespace-normalized for `git diff --check`.

The specific design references are SimpleCasual `StatusBar_Group_Light_ColorButton`, `Toggle_Switch_Light`, `Light/Lobby` UserInfo and `Light/Equipment` TabMenu. The project keeps its intentional Fredoka typography, custom currency/portrait artwork and existing theme. Layout corrections preserve the source-style rounded cards, light borders/shadows, icon proportions and selected tab underline.

`TaskTabsVerification` now verifies header/control/caption visibility, descendant-panel overlap and real tab raycast targets, instead of only checking tab geometry. It covers four resolutions with both sidebar states; 0/9999 displayed balances and a long name; all five tab corners/centres and keyboard submission; hover/press/release, disabled/re-enabled controls, page close/reopen, simulated focus-loss reset and rapid sidebar changes. It restores the previous test-store environment, start scene and Game View size/custom entries, with a bounded timeout. Canvas `pixelRect` and projected root bounds are checked against the requested resolution, avoiding inconsistent global `Screen` values from Editor callbacks.

Evidence for this follow-up is kept separately under `Library/ResponsiveUIVerification/`:

- `baseline-regression.txt`: the new verifier rejected the original clipped Pet Widget before the asset patch.
- `probe-integrity.txt`: 943 Canvas objects and 24,082 reference properties checked with no Missing Script/broken reference, plus CharacterHome bindings and all six navigation pages.
- `tabs-result.txt`, completed 2026-10-20 14:04:44 UTC: all eight resolution/sidebar combinations passed the expanded visibility, overlap, real raycast, keyboard and feedback checks. Fresh `tabs-*.png` images were inspected at 1920x1080, 1280x720, 1024x768 and 2560x1080; PNG dimensions were independently checked in `image-sizes.txt`. The two requested defects are resolved in rendered output. The chosen reference artwork was also opened for comparison.
- `content-wide-result.txt`, completed 14:08:31 UTC: a 100-character title, multiline description, 16 subtasks and ten tags exercised ultrawide details/editing, scroll bottoms, footer raycasts, invalid blank-title save and valid retry. Fresh top/bottom/error screenshots were inspected. No task-content panel covered the tabs.
- `content-narrow-editor-existing-failure.txt`: narrow expanded-sidebar details containment, footer raycasts and scrolling passed before the editor step exposed a **separate existing horizontal overflow**. Unchanged minimum widths (list 680 + editor 620 + gap 20 + padding 44 = 1364 units) require 840.01 screen pixels, overflowing the available area by about 45.1 pixels. The baseline snapshot has the same widths/flags. Long-subtask labels also visibly overlap in the retained details layout at both probed sizes. These are recorded follow-ups, not claimed visual passes; this change does not alter the editor's width or subtask typography.
- `task-flow-result.txt`, completed 14:09:28 UTC: the existing full TaskSystem Play verifier passed at the retained ultrawide Game View size, covering navigation, quick add, editing/cancel, tags, date/time, long titles/descriptions, completion confirmation, undo/history, scrolling, deletion and isolated disk persistence.
- `probe-restore.txt` and `probe-status.txt`: prior Play start scene, store environment and Game View settings restored; Editor stopped with no dirty scene. Both temporary probe scripts and their `.meta` files were removed after use. Their source snapshots remain only in the ignored evidence folder.
- `final-verification.txt`: Unity rebuilt the final Editor assembly at 14:11:00 UTC after removing the probes; subsequent state confirms compilation finished and Play Mode stopped on CharacterHome. Final `git diff --check` passed. No scene, vendor, Packages or ProjectSettings changes; Canvas SHA256 remained `3D1042CC537AF254B7C7C7CD8772A07257F4F9A6086F2A3518D675307F30ACC0` after the tests.

The native Companion process and real OS focus transitions were not exercised; the Pet Widget toggle's bounds/raycast were checked without launching a companion window. Focus-loss feedback testing explicitly simulated the existing lifecycle callback. Windows build testing is not required for these serialized UI layout changes.

## Follow-up: narrow task editor and wrapped subtasks

The user next authorized fixing the remaining editor overflow and long-subtask overlap. This supersedes the two open findings in the preceding follow-up; its failure records remain historical evidence.

Changes are limited to the relevant components:

- In Canvas's existing task-list header, the Search/Filter horizontal group now controls child widths. Search retains preferred width 300 with minimum 220; Filter retains its 80-unit width. This removes excess minimum width from the list column without changing the editor's minimum/preferred widths, field arrangement, typography or artwork.
- In Canvas's existing nested TaskDetails instance, the SubTasks section/list and first row (the runtime cloning template) use native layout width/height control. A flexible text LayoutElement lets TMP report wrapped preferred height; a fixed 30x30 LayoutElement preserves the checkbox size. The list's competing vertical fitter is disabled because its parent now allocates its height. Existing spacing, fonts, colours, checkmark art and objects remain intact. No new runtime component or duplicated per-page sizing logic was introduced.
- `ResisesSideMenuInLayoutGroup.LateUpdate()` updates content width when the container width changes, using the current sidebar width. A fresh test revealed that resizing with an unchanged toggle otherwise retained the old page width. This reuses the existing component and preserves its authored widths and animation.
- `TaskTabsVerification` now seeds isolated short/long/100-character subtasks, checks preferred text height, row nonoverlap, editor/search containment, and resizes an open editor without changing sidebar state. Existing interaction/feedback assertions remain.

Reference review used SimpleCasual `InputField_Light_Group`, `Toggle_Check_Light` and the Light Mission panel/preview. Actual editor and details captures were opened at all four sizes; original rounded panels, borders/shadows, palette, icon proportions and project Fredoka fonts remain. Save hover/press captures and Play checks confirm the shared `JuicyButton` feedback still animates and resets without moving its hit rectangle.

Current evidence lives in `Library/TaskContentLayoutVerification/`:

- `baseline-regression.txt`: before modification, the new wrapped-text assertion failed on the original long subtask at 1920x1080.
- `unchanged-sidebar-resize-failure.txt`: captured the stale content-width failure when resizing without toggling the sidebar; the existing resizer was then corrected.
- `content-result.txt`, completed 2026-10-20 14:26:20 UTC: all eight combinations of 1024x768, 1280x720, 1920x1080 and 2560x1080 with collapsed/expanded sidebar passed. Coverage includes 16 mixed-length subtasks, 100-character unbroken text, multiline description, ten tags, row height/nonoverlap, real checkbox/footer raycasts, scrolling to the bottom, Save hover/press/reset, keyboard invalid-title submission and successful retry. Captures include edit/details top/bottom, checked checkbox, hover/press and error states.
- `preservation.json`: all 3,598 existing serialized records retained, no renamed object or changed existing object-reference field, all 978 prior prefab overrides retained. Only 11 intended nested layout overrides and two LayoutElements were added (plus two Unity-generated references to the existing nested objects).
- `patch-idempotence.txt`: applying the targeted prefab edit twice produced an identical hash. No scene, source TaskDetails prefab, vendor content, font or existing `.meta` was replaced.
- `tabs-result.txt`, completed 14:28:14 UTC: expanded Task Tabs checks passed, including long-text sizing, narrow editor containment, empty History state, raycasts/keyboard, disabled/re-enabled feedback, simulated focus loss, close/reopen, rapid sidebar changes and direct resizing of an open editor with unchanged sidebar state.
- `task-flow-result.txt`, completed 14:29:10 UTC: existing full TaskSystem Play flow passed again, including edit/save/cancel, confirmation, quick add, tags, date/time, subtask scrolling, completion/undo/history and isolated persistence.
- `probe-integrity.txt`: 943 Canvas objects and 24,092 reference properties checked; no Missing Script or broken serialized reference. CharacterHome bindings and all six page switches also passed.
- `image-sizes.txt`: all 64 fresh screenshots have the requested pixel dimensions. Tests used isolated saves under Library. Native Windows/Companion behaviour was not changed or tested; resizing here was exercised in Unity's Game View.
- `probe-restore.txt`, `probe-status.txt` and `final-verification.txt`: original store environment, start scene and Game View settings restored; Editor stopped on CharacterHome with no dirty scene. Temporary probes and their `.meta` files were removed, retaining source snapshots only under ignored Library evidence. Unity rebuilt the final Editor assembly at 14:30:53 UTC with no compilation failure; final `git diff --check` passed. Four Unity-generated blank-name lines were whitespace-normalized without changing serialized values.
