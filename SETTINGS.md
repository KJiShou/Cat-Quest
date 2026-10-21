# Settings, tags and account

Open Settings from the existing gear in the main header. The modal uses the project's Fredoka font and the artwork/hierarchy of `Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Settings.prefab`; the package preview is `Preview/100_Settings.png`. The side actions are **Account** and **Tag Manage**. The Google sign-in button retains Google's supplied artwork as a branding exception.

## Controls and local persistence

- **Notification** enables Windows reminders for completed Focus and Rest timers. The switch saves immediately.
- **Sound** and **Music** independently adjust their assigned in-app audio sources from 0 to 100%; zero displays **Muted**. Dragging previews the volume; release, keyboard deselection, a short keyboard idle period or closing the view commits it. Failed saves show an error and restore the saved volumes.
- Close, Escape and the backdrop return from a subview or close Settings. Closing restores focus to the header gear. Tab/Shift+Tab stay within the active view, and keyboard selection scrolls tag actions into view.
- Settings use the existing local `catquest-data.json` transaction/backup path through `TaskService.SaveSettings`. Older version-5 saves receive additive defaults: notifications on, sound `0.594` (displayed as 59%), music zero. Existing task/progression data is retained.

## Tag Manage

The list shows each tag's color and current task-association count. **New tag** creates a tag; **Edit** changes its name and palette color. Names are trimmed, limited to 1–30 characters and unique ignoring case. The palette starts with the existing pale-green task chip and uses SimpleCasual blue, green, navy, orange and red colors.

Tasks reference `QuestTag.id`, never its displayed name or color. Renaming/recoloring updates current task rows, details, editing chips, tag headings and filter/dropdown indicators while retaining existing assignments and ID filters. A catalog refresh updates only the tag controls in an open task draft; unsaved text and blank subtask rows remain intact.

Delete requires confirmation showing the number of affected tasks. It detaches the tag from current tasks and open drafts while keeping tasks, completion history and rewards. A stale edit cannot recreate a deleted tag. Completed history retains its name/color snapshots; old history without saved colors uses the original pale-green default. The Tags task tab continues to group tasks; management stays in Settings.

## Account and privacy

Account supports a local, validated leaderboard display name (1–20 characters), Google browser sign-in, sign-out and optional statistics sharing. Until the connection asset is configured, the sign-in button is disabled with an explanation; local settings, tasks and progress remain available. Closing Account/Settings cancels an outstanding sign-in attempt.

Google sign-in alone does not publish data. Enabling sharing publishes only `playerId`, `playerName`, `bossStage`, `totalTasksCompleted`, `totalFocusMinutes`, `power`, `catLevel` and server `updatedAt`. Task titles/descriptions/schedules/tags, goals, responsibilities, AI content and detailed local history are excluded. The main Unity save remains authoritative; this is aggregate publication, not player-save backup or cloud restore. The separate [Leaderboard page](LEADERBOARD.md) reads these published aggregates after sign-in.

The first sharing opt-in binds this save to the Firebase UID. Sign-out retains local progress and that ownership binding. Wrong-account publication is refused. Another device's publication pauses sharing and offers an explicit comparison/replacement instead of merging totals. Turning sharing off stops uploads and queues removal; offline removal remains visibly pending. Network failure cannot undo local task, Focus, equipment or reward results.

Windows browser OAuth, Firebase Auth/Firestore configuration, credential storage, publication reconciliation, security rules and deployment steps are documented in [Firebase/SETUP.md](Firebase/SETUP.md). Firebase credentials and deployed services are user configuration; no Unity SDK package is required by this REST implementation.

## Windows reminders and audio authoring

The main Windows player registers the reminder icon while notifications are enabled. A normal Focus completion emits its reminder only after successful local settlement; a failed save emits none until a successful retry. Rest emits its reminder when its timer expires naturally. End Early keeps its approved settlement/Rest behavior but does not itself emit a Focus-completed reminder; Cancel, invalid/interrupted Focus and manually ending Rest do not emit completion reminders. A naturally completed Rest after End Early can still emit its Rest reminder.

Notifications contain only generic Focus/Rest text. Their click requests restoration/activation of the main window. The Companion process does not own or duplicate reminders. Events are deduplicated per live session; enabling notifications later does not replay disabled or failed reminders. Windows controls actual display and quiet-time behavior. The Editor does not display native reminders, and notification errors do not alter saved outcomes.

`Assets/Resources/SettingsAudio.prefab` contains independent Sound and Music `AudioSource` objects. Assign `musicClip` for looping background music and `previewClip` for the settings confirmation preview. Other project feedback can use `SettingsAudio.PlaySound` with an assigned clip. Clips are intentionally optional; unassigned clips produce silence. The Sound slider controls this in-app source; Windows notification sounds follow Windows settings.

The static views and tag row template are editable in `Assets/Prefabs/Settings/Settings.prefab`. `SettingsLauncher` on the existing Canvas gear instantiates one modal. Task chip tint bindings reuse the matching `BasicFrame_Oval_48_White` artwork while preserving existing chip geometry. Change affected serialized objects/properties directly; verification does not reconstruct or repair the prefab.

## Verification entrypoints

Use one flow at a time in the already-open project Editor. Test saves, screenshots and reports stay in ignored `Library/` or `Temp/` locations.

| Request / entrypoint | Checks and output |
| --- | --- |
| `Temp/verify-settings-data.request` | Runs the existing Focus → Rewards → Tasks chain, then settings persistence/bindings and account checks. Output: `Temp/verify-settings-data.result`; run folder under `Library/SettingsVerification/Data-*`. |
| `Temp/verify-settings-play.request` | Uses CharacterHome with an isolated save, captures the rendered vendor reference and Settings/Tags/Tag Editor/Account at 1920×1080 and 1280×720, exercises controls/motion/modal focus, audio preview/commit/reload, tag filters/drafts/deletion and empty/error states. Output: `Library/SettingsVerification/<timestamp-guid>/result.txt`, screenshots, and `latest-run.txt`. |
| **Tools > Cat Quest > Verify Settings Account**, or `Temp/verify-settings-account.request` | Isolated mocked account/aggregate/OAuth callback/conflict/removal checks. Output: `Temp/verify-settings-account.result` and `Library/SettingsAccountVerification/`. No real credentials or network calls. |
| `Temp/build-settings-windows.request` | Development Windows build with enabled project scenes. Player: `Builds/SettingsVerification/Build-*/`; reports: `Temp/build-settings-windows.result` and `Library/SettingsVerification/Build-*/`. A successful build alone does not establish native reminder behavior. |

The Play runner restores the previous save environment, Play Mode start scene, Game View selection/custom sizes, time scale, pause state and Editor scene setup. Editing scripts during a run aborts it with a failure; rerun after compilation finishes. No verifier saves temporary scene changes or resets the live save.

For the Windows notification check, run `Tools/Verify-SettingsNotifications.ps1 -Executable '<absolute path to the development CatQuest.exe>' -Run -AttemptBannerClick` in a fresh PowerShell process. The runner uses an isolated save and restores PlayerPrefs, the prior foreground window and cursor. It identifies the app's actual Focus/Rest shell banner, waits for its hit region to settle, clicks its title, and requires both a native activation callback and a restored foreground Unity window. A missing banner or activation fails the run; it never injects a synthetic notification callback. Windows quiet mode or competing notifications can prevent this interactive check, so run on an available desktop without simultaneous mouse input. Reports and cropped captures go under `Temp/SettingsVerification/native-*`.

Review fresh screenshots beside the reference and inspect the recorded motion frames. Separately verify native reminder appearance/click-to-restore and cleanup in the Windows player, and real Google/Firebase flows after configuration. The checks above describe the entrypoints; only a current run's reports and observed output establish which checks actually passed.
