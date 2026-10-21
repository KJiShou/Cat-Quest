# Cat Quest Unity Project

## Purpose and authority

- Cat Quest is a Windows desktop productivity-first Unity/C# application with gamified progression.
- Preserve approved behaviour and architecture. Prefer small, maintainable changes suitable for a single-student final-year project.
- The main Unity application and local persistence are authoritative for tasks, Focus outcomes, rewards, equipment and progression.
- Read the relevant implementation, callers, serialized bindings and feature documentation before editing. Do not invent paths, APIs, tests or implementation status.
- User instructions take precedence. This document incorporates the supplied project rules and the user's confirmed decisions on 2026-10-20: preserve End Early rewards, prioritize project-owned cleanup, and test proportionately to risk.

## Required workflow

1. Inspect Git status and relevant code, interfaces, data models, events, scenes and prefabs. Preserve unrelated work; do not create commits unless requested.
2. State the scope and affected files for nontrivial work. For UI work, identify the exact SimpleCasual reference component and the existing project component to reuse.
3. Make targeted edits. Do not rewrite unrelated systems or silently alter established mechanics.
4. Inspect the diff, including `.meta`, `.unity`, `.prefab`, `.asset`, `.controller`, `.asmdef`, `Packages/` and `ProjectSettings/`; run `git diff --check`.
5. Execute the applicable checks below. Fix failures caused by the change; identify pre-existing failures honestly. Never weaken assertions or restore obsolete UI merely to get a pass.
6. Report the behaviour change, checks actually executed, evidence locations and anything unverified. Historical logs and screenshots are context, not proof of the current revision.

## Unity asset safety

- Read `ProjectSettings/ProjectVersion.txt` and the installed package manifests; do not assume Unity/C# compatibility.
- Preserve existing `.meta` files and GUIDs. Move/rename assets with their `.meta`; delete the matching `.meta` only when the asset's deletion is intentional. Never regenerate existing GUIDs or create duplicates.
- Inspect code references, serialized GUIDs, UnityEvents, animation bindings, reflection/string lookups, Resources loading and automatic registration before removing or renaming scripts/assets. Zero textual class references alone do not establish that a script is unused.
- Preserve Inspector references/values, prefab variants/overrides and object names relied on by code. Consider `[FormerlySerializedAs]` for necessary serialized field renames and verify migration.
- Prefer Unity Editor APIs for necessary serialized changes; do not blindly replace asset YAML.
- Do not change the Editor version, backend, platform, API compatibility, dependencies, assembly definitions or ProjectSettings as incidental cleanup. Explain necessary changes and obtain authorization for disruptive work outside the task.
- Do not hand-edit Unity cache internals or generated `.csproj`/`.sln`, or commit caches/builds/logs. Existing verification request files and isolated test outputs in `Temp/` or `Library/` are permitted.
- Never use destructive Git cleanup/reset/force-push commands or overwrite unrelated user changes. Do not clear caches or bulk-delete third-party content as routine cleanup.

## UI design authority and review

- All product UI is based on `Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual`. Use its actual prefabs/components and their states, not merely a similar palette or one rounded sprite.
- Before implementation, name the specific source prefab/component and inspect its hierarchy, artwork, typography, spacing and states. Prefer an existing project component; extend vendor components through project prefabs/variants without altering vendor originals.
- `.impeccable.md` describes global design context and separately scoped Companion requirements. Companion-only layout restrictions do not apply to all pages.
- Preserve established project typography, including intentional custom font choices, and user-authored adjustments. Record relevant deviations; do not perform an unsolicited global font/style replacement.
- After EVERY UI change, inspect actual rendered before/after output beside the chosen reference. Check typography hierarchy, shape, borders, shadows, colour, spacing, icon proportions, selected/disabled states and composition. A matching sprite GUID or automated geometry check is insufficient.
- Check clipping, overlap, raycast blocking, modal dismissal, focus navigation and relevant empty/error states. Capture and inspect ordinary application UI at 1920x1080 and 1280x720. Shared responsive layout/navigation changes additionally require 1024x768 and 2560x1080, with expanded/collapsed sidebar.
- Companion layout changes require its actual supported compact/minimum and normal window sizes. Native behaviour requires the Windows checks below.
- If rendering/interaction cannot be observed, explicitly report visual/interaction verification as incomplete; do not claim design-language compliance from code inspection.

## Juicy feedback and reuse

- Reuse/extend existing feedback components such as `JuicyButton`, `JuicyButtonMesh` and `PlayerHealthFeedback`; avoid competing per-page implementations or multiple feedback owners on one control.
- Verify normal, hover, press, release, keyboard selection/submit and disabled states where applicable, including rapid repeated input, pointer exit, page closure, disable/re-enable and application focus loss.
- Animation must settle and reset, keep hit areas and layout stable, avoid cumulative scale/position changes and not block input. Use unscaled time where UI should remain responsive when gameplay time is paused.
- Inspect motion in Play Mode or a short capture; screenshots alone do not establish animation quality. Check toggles/sliders and other changed controls too, not only controls already carrying a `JuicyButton`.
- Important success, failure and state changes need appropriate feedback. Success/reward presentation follows a committed domain result; animations never grant rewards, duplicate actions or become the authority for state.
- Presenters handle display and input; existing services own rules, state transitions and persistence. Reuse real shared behaviours without adding speculative frameworks, managers or abstractions.
- New persistent static UI belongs in editable project prefabs with serialized bindings. Dynamic content uses reusable templates. When changing static portions of existing runtime-generated pages, persist the affected component instead of regenerating the whole page; do not migrate unrelated pages.

## Preserve manual UI edits and retire generators

- Modify only the requested component/subtree and necessary properties. Keep object identity, unrelated children, values, bindings and overrides.
- Do not clear an existing page, create a replacement root, overwrite an existing prefab from a new hierarchy, or rerun whole-page generation to implement a local change.
- The retained whole-page build paths in `DashboardRewardSetup`, `FocusPageSetup.ConfigurePage`, `CompanionPageSetup.Build`, `CompanionWindowDesign.Build` and `BossPageSetup.Build` are legacy creation tools: do not use them to modify existing UI. Their presence is not permission to regenerate pages.
- Preserve separately useful Boss stage synchronization, Windows build, animation baking and validation. Check a tool's actual responsibilities and callers before deleting it; names such as `Setup`, `Editor` or `Builder` are not deletion evidence.
- Reusable Editor tools must target explicit objects/properties, be safe to repeat, preserve unrelated changes, and support Undo for interactive scene edits where applicable. Verify running twice does not duplicate components/listeners or overwrite unrelated edits.
- After a one-time generator has saved its outputs, validate their bindings and behaviour, resolve helper dependencies, then remove the unused script and `.meta`. Remove temporary probes after their checks too.
- A verification command must not repair/regenerate authored assets. Keep generation/repair explicit and separate from checking.

## C# implementation

- Keep runtime code independent of `UnityEditor`; use an Editor folder/assembly or correct conditional compilation for tooling.
- Use Unity APIs on the main thread. Background I/O/computation must marshal scene/UI/object work back to it.
- Respect lifecycle and ownership. Subscribe/unsubscribe symmetrically and prevent duplicate listeners, unmanaged coroutines and leaked temporary resources.
- Avoid unnecessary per-frame allocation, repeated global searches, blocking main-thread I/O/network calls and unbounded logging.
- Use existing services/interfaces and follow local naming/patterns. Do not add packages, services, frameworks or broad abstractions without a demonstrated need.

## Gameplay and data contracts

- Productivity comes first. Manual task creation/completion must not require gameplay or working AI/network access.
- Daily Tasks recur; unfinished occurrences become Missed at reset without completion rewards. Overdue Todo items remain active until completed/deleted.
- AI Planning proposes editable suggestions and advisory difficulty requiring user confirmation. AI never calculates or grants EXP, Fish Coins, Boss Tickets or equipment.
- Task completion uses local rules, user-confirmed difficulty and existing caps. Record completion even at the cap and prevent duplicate settlement. Preserve existing amounts/caps; treat unspecified balance as provisional rather than inventing it.
- Preserve the approved distinction between **End Early** and **Cancel/interruption**. End Early settles earned time under existing reward/cap rules, contributes to the daily Focus goal and starts Rest after successful persistence. Cancelled/invalid/interrupted sessions grant no rewards. Do not reinstate the older reference document's prohibition on End Early rewards.
- Raw Focus Boss Tickets use `floor(completedFocusMinutes / 25)` subject to current allowances/caps. Rest grants no EXP, Fish Coins or tickets. Aura/fish spectator effects are presentation-only.
- Main Unity owns Focus timing, settlement, Rest and persistence. Companion is a presentation/control client; validate session/owner/request identity and prevent duplicate commands/settlement.
- Keep three equipment slots: Weapon (ATK), Body Outfit/Armour (DEF), Head Accessory (ASPD). Equipment level cannot exceed Cat Level.
- Boss battles are deterministic for the same valid snapshot/configuration. Confirmed entry consumes one ticket; valid defeat consumes the entry, valid victory progresses. Technical interruption must not fabricate outcomes; preserve the existing exactly-once interrupted-entry recovery.
- Boss 2 requires a valid Boss 1 victory and Cat Level >= 10; Boss 3 requires a valid Boss 2 victory and Cat Level >= 20. First valid Boss 3 victory unlocks Master Stage.
- Normal bosses have independent mastery; wins 5/10/15 each allow one unowned component from that boss's three-piece set. Prevent duplicate claims. Master Stage does not grant normal-boss mastery equipment.
- Master Stage Boss = `1 + ((stageNumber - 1) % 3)`; only victory advances the stage.
- Persist locally in `catquest-data.json` or the configured location. Preserve compatibility and atomic, duplicate-resistant transitions. Any player-data reset/migration requires an explicit safe plan and backup; tests never use the live save.
- Optional leaderboard uploads contain only approved aggregates and a user-selected display name, never private tasks, schedules, goals, AI prompts/responses or detailed productivity history. Network failure cannot undo valid local progress.

## Risk-based testing

| Change | Required checks |
| --- | --- |
| Documentation only | Check instructions against implementation and references; review diff. |
| Code or asset cleanup | References, actual Unity compilation when available, affected behaviour/bindings, missing scripts and serialized diff; `git diff --check`. |
| UI/layout | Rendered reference comparison and interaction/motion checks above at the applicable sizes. |
| Domain/rewards/persistence/progression | Narrowest relevant existing verifier; add meaningful regression for a new behaviour/failure. Cover success, failure/retry, duplicate invocation, cap/boundary, reload and cancellation/interruption where relevant. |
| Native Companion/startup/build | Windows development build smoke and actual companion process: applicable close/minimize/resize/topmost behaviour, disconnect/heartbeat loss, wrong-owner/stale/duplicate commands. |

- Use existing custom Editor checks; routine styling does not require a new test framework or a Windows build. Do not add tests that merely mirror implementation.
- The current aggregate chain is `EquipmentVerification` -> `BattleVerification` -> `FocusImplementationVerification` -> `RewardSystemVerification` -> task and CharacterHome checks. Run the appropriate entrypoint once, not every parent and child redundantly.
- UI runners include Dashboard, Boss, Task Tabs, Equipment and Button Pages verification. Select the relevant flows; feedback checks do not replace visual review.
- Check Editor state before running; use the already-open project instance rather than launching a competing instance. Run one verification flow at a time.
- Use injected clocks/isolated saves and bounded runs. Preserve/restore the prior environment, Play Mode start scene and Game View settings on completion or failure. Never overwrite player data or save temporary test scene changes.
- Failures must propagate to aggregate results. Missing/stale results are not passes. Record the current run's time/context, exact checks and artifact locations in ignored output directories.
- Screenshots/results belong in `Library/` or `Temp/`, not authored `Assets/`. Review Git status after tests for unintended changes.
- If unavailable, explicitly state "Unity compilation / Play Mode / build not verified", narrowing the statement to the checks actually unavailable. `dotnet build` alone does not prove Unity compatibility.

## Completion report

- **Changed:** files and observable behaviour.
- **Preserved:** relevant existing behaviour, authored UI and data contracts.
- **Validated:** exact checks actually performed and their results/artifacts.
- **Not validated / risks:** remaining gaps and why.
- **Manual checks:** only the specific Unity/Inspector/scene/window checks still required; do not imply they already passed.
