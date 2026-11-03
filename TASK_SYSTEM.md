# Task system and productivity rewards

[English](TASK_SYSTEM.md) | [简体中文](TASK_SYSTEM.zh-CN.md)

Open `Assets/Scenes/CharacterHome.unity` for the main UI, or start through `CatQuestBootstrap.unity` for onboarding. Existing player names and progress are retained. The sidebar provides Dashboard, Tasks, Chat, Focus, Boss, Cat Home and Leaderboard. Later features are implemented, not placeholder containers. Feature gates are described in [Tutorial](TUTORIAL_SYSTEM.md).

## Task views and interaction

| View | Contents |
| --- | --- |
| Today | Today's and overdue unfinished scheduled Todo items, plus eligible unfinished daily occurrences |
| Daily Tasks | Daily templates and today's completion state. Future-start templates cannot be completed early |
| Todo | Unfinished one-off and daily tasks, grouped by date |
| Tags | Active tasks grouped by tag, including multiple groups for multiple tags and No Tags for untagged tasks |
| History | Completion records, newest first, with read-only details |

The sidebar retains its selected purple/white `Button_Nav` styling after pointer exit or focus changes. Switching pages commits nonempty quick input and discards unsaved edit drafts. Returning to Tasks refreshes the date. In List it opens the saved default tab (Today, Daily Tasks or Todo); an active Calendar retains its current view. Explicit task links still select their destination.

Without a selection, the list fills the page. Selecting a row opens details, and Edit opens an independent draft. A tab/filter change or completion that removes the selected item restores the full-width list. Clicking outside rows/details closes ordinary details but does not discard an edit draft or interfere with an open modal. Descriptions wrap to available width and empty descriptions reserve no blank area.

The **+** action opens quick title input. Enter saves a nonempty title and allows another entry. Clicking elsewhere saves nonempty input and closes it. Empty input creates nothing and Escape cancels. Only **Save Changes** commits edits. Cancel/close discards them and blank subtask rows are excluded.

Today defaults to today's date, Todo to no schedule and Daily Tasks to daily repetition. Repetition requires a start date. Scheduling is date-only. History still shows actual completion date/time. Legacy `minuteOfDay` is retained when editing but does not control validation, ordering or overdue status. No save reset is needed.

Subtask checkboxes save immediately. Completing a task with unfinished subtasks requires confirmation, then completes them together. The row checkbox completes without selecting its row. History and today's completed daily items allow eligible undo without confirmation. Older daily occurrences and deleted tasks cannot be restored. Undo keeps subtask progress and paid rewards. Recompletion restores/updates the original history receipt without paying again.

Todo becomes overdue at local midnight after its scheduled date, shown in red. Daily tasks use today's occurrence and midnight reset rules. Unfinished daily occurrences become Missed without rewards. Equal-date items sort by title. Daily state is created on first access and refreshed at midnight, return or restart, without fabricating every offline date. Template edits affect current unfinished and future occurrences, not completed history.

Deletion requires confirmation. Removing a daily template stops future recurrence while retaining history/rewards. Deleting a tag detaches current associations without changing history snapshots. Tag management is in Settings. Search matches title, description and tags. Difficulty and multiple tag filters combine. Today/Daily/Todo badges show each full collection, not the filtered count. Tags/History have no count badge. A saved task excluded by the filter remains saved even when selection clears.

Todo uses nonempty Due, Daily, Today, Tomorrow, This Week, Next Week, This Month, Next Month, Later and No Date groups, with the existing Tags heading treatment. Due means before today. Weeks run Monday–Sunday; the most specific matching group wins, so tasks are never duplicated across date groups. Search/difficulty/tag filters apply before grouping. Future daily templates stay in Daily with the existing eligibility restriction. The Todo badge includes its unfinished daily tasks; the service's one-off `Query(TaskTab.Todo)` contract remains unchanged.

Dashboard keeps Daily Tasks and Today's Tasks (including overdue Todo items), then adds only nonempty upcoming date/Later/No Date groups. Clicking any one-off task opens and selects it in Todo. Task completion, rewards and recurrence rules are unchanged.

## Calendar view

Tasks has a List / Calendar switch. List keeps the five existing tabs and the configured default tab; Calendar is a separate month view with independent search, difficulty/tag filters and a Show completed toggle (on by default). View/month/selection survive navigation within the process, without adding save fields.

The calendar starts on Monday and shows six weeks. Todo stays on its scheduled date, including overdue items. Completed Todo uses the original receipt snapshot schedule, not its completion date. No Date contains unscheduled Todo. Clicking a date opens a day drawer; clicking a title opens the existing details presentation. New tasks inherit the selected date (or no schedule from No Date). Editing, completion, confirmation, deletion and eligible undo use the existing task service. Save failure keeps the draft open.

Today’s Daily entries are actionable. Future Daily entries are read-only previews from current templates and their start dates; Edit daily task edits the template without changing today’s subtask progress. Previews cannot complete or update subtasks. Past days show saved completion snapshots and aggregate saved missed occurrences only. Offline gaps are not fabricated; missed aggregates are hidden with metadata filters because old occurrences lack historical metadata. Calendar queries never persist or create occurrences.

The drawer appears beside the month when both Canvas space and rendered width allow it; otherwise it overlays the month. This accounts for Canvas scaling at 1280×720. Only the drawer scrolls. Arrow keys navigate focused dates, Enter opens one, Page Up/Down changes month, and Escape backs out of the topmost calendar state. Editing blocks the month and view switch.

Calendar assets are editable project prefabs under `Assets/Prefabs/Task/Calendar`. The view switch retains Tasks/TabMenuFrame styling; controls reuse SimpleCasual Button_Round01 and the existing task date picker, row, details and editor. Do not regenerate the Tasks page.

Calendar projection regressions run within Verify Task System (and its existing aggregates). `Temp/verify-task-calendar.request` runs the isolated calendar Play flow; results and screenshots are in `Library/TaskCalendarVerification/`. Verification does not install or repair UI assets.

## Ownership and persistence

`GameDataManager` owns the shared store and `TaskService`, retaining SetUsername/GetUsername/HasUsername/SaveData APIs. `TaskSystemUI` binds the list, details, tabs, HUD, search and dialogs. `EditTaskDetailsUI` owns only its draft.

- `QuestTask` defines tasks. Tasks, subtasks and tags have stable GUIDs.
- `TaskOccurrence` records subtask progress, completion and reward settlement. Its day is `once` for Todo or local `yyyy-MM-dd` for daily tasks.
- `TaskCompletion` snapshots title, description, tags, subtasks, difficulty and actual rewards. `revoked` tracks undo, defaulting to false for older saves. Recompletion updates the completion snapshot but retains original paid amounts.
- `TaskService` handles Upsert, Delete, SetSubtask, Complete, UndoCompletion, CanUndoCompletion, IsOverdue, SaveTag, DeleteTag, Query and RefreshDate. Undo validates the history ID, surviving task and eligible date. Mutations copy, validate and save before Changed is emitted. Failed writes do not replace committed memory.

Save: `Application.persistentDataPath/catquest-data.json`. Current Windows default: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Cat Quest\catquest-data.json`.

Writes use `.tmp`, atomic replacement and a rolling `.bak`. An unreadable main file falls back to a valid backup with a notice. Recovery preserves the corrupt original as `.corrupt.<GUID>` rather than overwriting a good backup with corrupt content. Username, tasks and progression share the local save. Schema 5 preserves earlier username-only, task and Focus schemas. Online publication is not cloud-save restoration.

## Rewards and daily goals

At Lv.1, Easy/Medium/Hard pay **15/25/40 EXP** and **6/10/16 Fish Coins**. The saved Normal enum remains compatible and displays as Medium. Each Todo pays once and each daily occurrence pays once per date. Task daily bases are **150 EXP and 60 Fish**. Amounts and caps scale with the pre-settlement level using `1 + 0.1 × (level - 1)`, then floor and clip each currency to its remaining allowance. Completion is recorded even at the cap. The first valid first-time settlement each day gives one automatic Ticket independently of currency caps. Undo/recompletion cannot duplicate it. New saves start at zero, not scene mock balances.

At Lv.30, Medium pays 97 EXP/39 Fish and task caps are 585 EXP/234 Fish. Previews share settlement math. Paid currency, including revoked receipts, consumes allowance. Undo does not release allowance.

Dashboard shows the real name, level, within-level EXP, equipped slots and authoritative stats/Power. Its LV/green bar derives from `Lobby_Popup_User_Info`. Levels start at 1 and require `100 + 25 × (current level - 1)` EXP per level. One reward can cross multiple levels.

The existing two Daily Mission Rewards cards use Mission visuals and Claim/In Progress/Claimed states:

| Goal | Base bonus |
| --- | --- |
| First completed task | 10 EXP + 5 Fish |
| Three distinct task occurrences, after the first goal | An additional 20 EXP + 10 Fish |
| 25 accumulated eligible Focus minutes | 10 EXP + 5 Fish |

Each stage is claimable once daily. Bonuses scale/floor at claim level and do not consume base allowances. Todo and daily occurrences count, subtasks do not. Undo lowers unclaimed progress but keeps paid bonuses. Recompletion cannot pay twice. Local midnight resets goals without carrying unclaimed bonuses forward. End Early seconds count toward Focus goals, with displayed progress floored to minutes and legacy-minute fallback. The automatic first-task Ticket requires no Claim click.

## Focus settlement

Pomodoro uses 1–60 minute Focus/Rest selectors, default 25/5. Stopwatch counts up beyond three hours. Countdown accepts 1–180 minutes. Only Pomodoro enters Rest after successful settlement. The current Pomodoro UI offers confirmed Cancel, while Stopwatch and Countdown offer confirmed End Focus to settle earned time. The underlying and historical End Early rules remain supported.

Currency bases are 0.5 EXP per complete minute and 1 Fish per complete 5 minutes, multiplied using the session-start level before flooring. Only the first 180 minutes per session and 240 minutes per day earn currency across all modes. Tickets use complete 25-minute intervals with a separate 3-per-day Focus cap. Short sessions are not joined for ticket/fish intervals. A valid 25-minute session can still meet the daily goal after currency limits are exhausted.

The persistent main controller keeps time through page/application changes. Sleep, suspension or an unverifiable update gap over 10 seconds invalidates active Focus. Restart does not reward unfinished sessions. Cancel/invalid/interrupted sessions grant nothing and do not start Rest. Save failure freezes settlement for Retry Save without duplicate rewards. Rest grants nothing and can end early. Sessions crossing midnight belong to the settlement day. See [Focus and Companion](FOCUS_COMPANION.md).

## Compatibility and level-up gifts

Reward changes dated 2026-10-31 add fields without changing schema 5. Existing balances, receipts and paid daily totals remain even if above new caps. Already-used legacy task days do not gain the new first-task Ticket/goal retroactively. The next day is eligible. Existing Focus goal receipts retain their amounts. Loading alone does not rewrite the save.

Each newly reached level L gives `5 × L` Fish. Multiples of five also give one Ticket. Multi-level gains sum gifts, committed atomically with the triggering reward outside normal allowances. Loading, failed saves, duplicates and undo/recompletion do not backfill or repay them.

`Assets/Prefabs/LevelUpCelebration.prefab` embeds SimpleCasual Light/LevelUp with project HUD font/icons and JuicyButton. After committed reward feedback, unscaled fade, badge pop and rotating rays present the reached level and total gifts. Continue/Escape dismiss only presentation and notifications queue. Goal labels remain serialized in Canvas.

## Header allowance preview

Hover Fish, Ticket or the avatar to see the corresponding allowance. Fish/EXP show Task and Focus bars. Ticket shows paid Task + Focus Tickets out of 4 with the separate 1 + 3 limits. Task bars use level-scaled caps, Focus bars use rewarded minutes and the shared formula at the active reward level. Daily goals and level-up gifts are extra.

`TaskService.TodayRewards` drives the read-only tooltip, refreshed on service changes and dismissed on exit/press, navigation, disable or focus loss. `Assets/Prefabs/RewardLimitTooltip.prefab` retains the EquipmentItemDetail background/shadow/TopFrame and header font. Sliders reuse UserInfo/Slider_Green from SimpleCasual SliderGroup_UserInfo_Light. They have no navigation/callbacks/raycast capture and ignore parent layout. All header targets are serialized without rebuilding the page.

## Optional AI

Chat queries tasks and proposes creation/edits requiring confirmation. Difficulty is manual, new proposals default to Medium and edits preserve prior values. `TaskService.ConfirmChatDraft` reuses atomic upsert and rejects stale, deleted, settled, duplicate and cross-day proposals. Only requested task facts are shared. See [AI](AI_IMPLEMENTATION.md).

## Verification

Use isolated saves in the already-open Editor and restore prior state. These are available entrypoints, not results of this documentation update.

| Entry point | Coverage and output |
| --- | --- |
| Tools → Cat Quest → Verify Task System | CRUD, drafts, filters/tags, deduplication, undo/history, dates, midnight, legacy data, rollback/backup. `Temp/task-system.verification` |
| Tools → Cat Quest → Verify MainMenu Play Flow | Historical menu name for the existing task Play runner. UI and persistence using `Library/TaskSystemPlayVerification/<GUID>/save.json`. `Temp/task-system.play-result` |
| Tools → Cat Quest → Verify Rewards and Focus | Rewards, caps/goals, undo, midnight, settlement/retry/cancel/Rest, migration/levels plus task/CharacterHome checks. `Temp/reward-system.verification` |
| Tools → Cat Quest → Verify Dashboard Rewards Play Flow | Claims, navigation, Focus, HUD, Rest/cancel/reload and 1280×720/1920×1080. `Temp/dashboard-reward-play.verification`, `Library/DashboardRewardPlayVerification/<GUID>/` |
| `Temp/verify-reward-balance.request` | Isolated balance checks, LevelProgression and Equipment → Battle → Focus → Rewards → Tasks/CharacterHome. `Temp/verify-reward-balance.result` |
| `Temp/verify-reward-tooltip.request` containing `after` | Allowance tooltip Play checks at both standard sizes in `Library/RewardLimitTooltipVerification/`. `before` captures closed tooltips |

Keep saved Canvas components editable and do not regenerate whole pages. Missing, stale or failed results are not passes.

## Calendar button presentation

Today uses a blue date marker independently of the selected-date background. List / Calendar uses navy with white text for the selected view and a light background with deep ink for the other view. Today and Add Task use blue primary buttons, Filter uses sky blue, No Date uses aqua and Clear uses blue-gray. Compact sliced buttons use multiplier 2 with reduced shadows; see [design context](.impeccable.md#compact-button-appearance).

The grid labels future recurrence counts as `Daily · N`; opening one explains its preview status. Preview means a future Daily projection, not a saved occurrence or an opportunity to complete early.
