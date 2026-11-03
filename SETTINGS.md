# Settings, tags and account

[English](SETTINGS.md) | [简体中文](SETTINGS.zh-CN.md)

Open Settings from the existing gear in the main header. The modal uses the project's Fredoka font and the artwork/hierarchy of `Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Settings.prefab`. The package preview is `Preview/100_Settings.png`. The side actions are **Account**, **Manage tags** and **AI**. The Google sign-in button retains Google's supplied artwork as a branding exception.

## Controls and local persistence

- **Notification** enables Windows reminders for completed Focus and Rest timers. The switch saves immediately.
- **Sound** and **Music** independently adjust their assigned in-app audio sources from 0 to 100%. Zero displays **Muted**. Dragging previews the volume. Release, keyboard deselection, a short keyboard idle period or closing the view commits it. Failed saves show an error and restore the saved volumes.
- Click the circular Sound/Music icon to mute or unmute that channel immediately. Unmute restores its last nonzero volume during this application session, including after closing/reopening Settings; if launched already muted, it restores 100%. The muted volume is saved through the same transaction and failure rollback as the sliders.
- **Default task tab** chooses Today, Daily Tasks or Todo for entering Tasks. It saves immediately and restores the committed choice on failure. Older or invalid settings default to Today. Direct links to tasks override this entry preference.
- **Dashboard leaderboard** selects Boss Stage, Tasks Completed, Focus Minutes or Power. It saves immediately and controls the Dashboard preview and View All destination. Older saves default to Focus Minutes.
- **Show on Dashboard** independently controls Tasks, Daily rewards, Focus, Leaderboard and Character. All start visible. Changes save immediately. Failed writes restore the committed switches. Hidden cards leave the layout and empty rows collapse. When no top cards remain, the quick-action row keeps its compact height. With all five hidden, a short message points back to Settings. These settings affect only Dashboard presentation: hiding Focus does not cancel its timer, and hidden features remain available from their sidebar pages. The chosen ranking category is retained while its card is hidden.
- Close, Escape and the backdrop return from a subview or close Settings. Closing restores focus to the header gear. Tab/Shift+Tab stay within the active view, and keyboard selection scrolls tag actions into view.
- Settings use the existing local `catquest-data.json` transaction/backup path through `TaskService.SaveSettings`. New players and older saves without settings receive defaults: notifications on, sound and music `1.0` (100%). Existing saved volumes, including mute, and task/progression data are retained.

## Tag Manage

The list shows each tag's color and current task-association count. **New tag** creates a tag. **Edit** changes its name and palette color. Names are trimmed, limited to 1–30 characters and unique ignoring case. The palette starts with the existing pale-green task chip and uses SimpleCasual blue, green, navy, orange and red colors.

Tasks reference `QuestTag.id`, never its displayed name or color. Renaming/recoloring updates current task rows, details, editing chips, tag headings and filter/dropdown indicators while retaining existing assignments and ID filters. A catalog refresh updates only the tag controls in an open task draft. Unsaved text and blank subtask rows remain intact.

Delete requires confirmation showing the number of affected tasks. It detaches the tag from current tasks and open drafts while keeping tasks, completion history and rewards. A stale edit cannot recreate a deleted tag. Completed history retains its name/color snapshots. Old history without saved colors uses the original pale-green default. The Tags task tab continues to group tasks. Management stays in Settings.

## Account and privacy

Account supports a local, validated display name (1–20 characters). Saving it updates the player username and leaderboard name together in one local transaction; a failed save preserves both previous names. Account also supports Google browser sign-in, sign-out and optional statistics sharing. Until the connection asset is configured, the sign-in button is disabled with an explanation. Local settings, tasks and progress remain available. Closing Account/Settings cancels an outstanding sign-in attempt.

Google sign-in alone does not publish data. Enabling sharing publishes only `playerId`, `playerName`, `bossStage`, `totalTasksCompleted`, `totalFocusMinutes`, `power`, `catLevel` and server `updatedAt`. Task titles/descriptions/schedules/tags, goals, responsibilities, AI content and detailed local history are excluded. The main Unity save remains authoritative. This is aggregate publication, not player-save backup or cloud restore. The separate [Leaderboard page](LEADERBOARD.md) reads these published aggregates after sign-in.

The first sharing opt-in binds this save to the Firebase UID. Sign-out retains local progress and that ownership binding. Wrong-account publication is refused. Another device's publication pauses sharing and offers an explicit comparison/replacement instead of merging totals. Turning sharing off stops uploads and queues removal. Offline removal remains visibly pending. Network failure cannot undo local task, Focus, equipment or reward results.

Windows browser OAuth, Firebase Auth/Firestore configuration, credential storage, publication reconciliation, security rules and deployment steps are documented in [Firebase/SETUP.md](Firebase/SETUP.md). Firebase credentials and deployed services are user configuration. No Unity SDK package is required by this REST implementation.

## Windows reminders and audio authoring

The main Windows player registers the reminder icon while notifications are enabled. A normal Focus completion emits its reminder only after successful local settlement. A failed save emits none until a successful retry. Rest emits its reminder when its timer expires naturally. End Early keeps its approved settlement/Rest behavior but does not itself emit a Focus-completed reminder. Cancel, invalid/interrupted Focus and manually ending Rest do not emit completion reminders. A naturally completed Rest after End Early can still emit its Rest reminder.

Notifications contain only generic Focus/Rest text. Their click requests restoration/activation of the main window. The Companion process does not own or duplicate reminders. Events are deduplicated per live session. Enabling notifications later does not replay disabled or failed reminders. Windows controls actual display and quiet-time behavior. The Editor does not display native reminders, and notification errors do not alter saved outcomes.

`Assets/Resources/SettingsAudio.prefab` contains independent Sound and Music `AudioSource` objects. Assign `musicClip` for looping background music and `previewClip` for the settings confirmation preview. The same component is the application sound manager, with optional Focus/Rest/Battle music and button, task, level-up, battle and timer cues. See `SOUND.md` for the drag-and-drop field list and playback rules. Other project feedback can use `SettingsAudio.PlaySound` with an assigned clip. Clips are intentionally optional. Unassigned clips produce silence. The Sound slider controls this in-app source. Windows notification sounds follow Windows settings.

The static views and tag row template are editable in `Assets/Prefabs/Settings/Settings.prefab`. `SettingsLauncher` on the existing Canvas gear opens its serialized, pre-placed modal instance. Task chip tint bindings reuse the matching `BasicFrame_Oval_48_White` artwork while preserving existing chip geometry. Change affected serialized objects/properties directly. Verification does not reconstruct or repair the prefab.

Settings and its Account/Tag subviews use the project's blue action buttons, blue text/input accents, and the neutral SimpleCasual `Toggle_Switch_WhiteCustom` and `Slider15_WhiteCustom` artwork tinted blue. Switch handles have distinct active/muted blue colors. Sound/Music handles, fills and tracks use the same palette, including zero volume. Gear icons retain their original artwork/colors. Tag palette colors remain meaningful user data, error messages retain their error color, and Google's supplied sign-in artwork remains the branding exception.

## Verification entrypoints

Use one flow at a time in the already-open project Editor. Test saves, screenshots and reports stay in ignored `Library/` or `Temp/` locations.

| Request / entrypoint | Checks and output |
| --- | --- |
| `Temp/verify-settings-data.request` | Runs the existing Focus → Rewards → Tasks chain, then settings persistence/bindings and account checks. Output: `Temp/verify-settings-data.result`. Run folder under `Library/SettingsVerification/Data-*`. |
| `Temp/verify-settings-play.request` containing `mute` | Focused Sound/Music icon raycast clicks, hover/press/reset, keyboard unmute, independent channels, rapid toggles, reopen and immediate save/reload at 1920×1080 and 1280×720. Uses the same isolated runner/output and restoration. |
| `Temp/verify-settings-play.request` containing `task-dates` | Focused date grouping, default-tab persistence/failure/retry, UI navigation and feedback, empty groups, plus 1920×1080/1280×720 screenshots. Uses the same isolated runner/output and restoration below. |
| `Temp/verify-settings-play.request` | Uses CharacterHome with an isolated save, captures the rendered vendor reference and Settings/Tags/Tag Editor/Account at 1920×1080 and 1280×720, exercises controls/motion/modal focus, audio preview/commit/reload, ranking choice, five component switches, blue Account states, tag filters/drafts/deletion and empty/error states. Also checks all 32 Dashboard visibility combinations at 1920×1080, 1280×720, 1024×768 and 2560×1080 with both sidebar states. Output: `Library/SettingsVerification/<timestamp-guid>/result.txt`, screenshots, and `latest-run.txt`. |
| **Tools > Cat Quest > Verify Settings Account**, or `Temp/verify-settings-account.request` | Isolated mocked account/aggregate/OAuth callback/conflict/removal checks. Output: `Temp/verify-settings-account.result` and `Library/SettingsAccountVerification/`. No real credentials or network calls. |
| `Temp/build-settings-windows.request` | Development Windows build with enabled project scenes. Player: `Builds/SettingsVerification/Build-*/`. Reports: `Temp/build-settings-windows.result` and `Library/SettingsVerification/Build-*/`. A successful build alone does not establish native reminder behavior. |

The Play runner restores the previous save environment, Play Mode start scene, Game View selection/custom sizes, time scale, pause state and Editor scene setup. Editing scripts during a run aborts it with a failure. Rerun after compilation finishes. No verifier saves temporary scene changes or resets the live save.

For the Windows notification check, run `Tools/Verify-SettingsNotifications.ps1 -Executable '<absolute path to the development CatQuest.exe>' -Run -AttemptBannerClick` in a fresh PowerShell process. The runner uses an isolated save and restores PlayerPrefs, the prior foreground window and cursor. It identifies the app's actual Focus/Rest shell banner, waits for its hit region to settle, clicks its title, and requires both a native activation callback and a restored foreground Unity window. A missing banner or activation fails the run. It never injects a synthetic notification callback. Windows quiet mode or competing notifications can prevent this interactive check, so run on an available desktop without simultaneous mouse input. Reports and cropped captures go under `Temp/SettingsVerification/native-*`.

Review fresh screenshots beside the reference and inspect the recorded motion frames. Separately verify native reminder appearance/click-to-restore and cleanup in the Windows player, and real Google/Firebase flows after configuration. The checks above describe the entrypoints. Only a current run's reports and observed output establish which checks actually passed.

## Optional ChatGPT connection

Settings → **AI** is separate from the Google leaderboard account. **Continue with
ChatGPT** opens the system browser using this installation's own OAuth registration.
Closing AI settings cancels pending sign-in. An established connection remains
available to task pages. **Disconnect ChatGPT** clears the local tokens and account registration and attempts
remote session revocation, with an explicit message when remote revocation is unconfirmed.
Select **Continue with ChatGPT** again to authorize another account on the same installation.

Chat sends user messages and relevant task information requested during the
conversation to OpenAI using the user's eligible ChatGPT plan. Task proposals
require confirmation, and difficulty stays manual. The user chooses an
account-supported model and can open **Manage usage**. Local tasks remain usable
without a connection or available quota. Chat history is stored separately in
`ai/chat.dpapi`, and can be cleared without deleting tasks.

Configuration lives in the application data directory's `ai/preferences.json`.
installation identity is in `ai/host.json`, and `ai/connection.dpapi` is protected
by Windows current-user DPAPI. These records do not change `catquest-data.json`.
See `AI_IMPLEMENTATION.md` for protocol, test entrypoints and isolated Windows
smoke instructions. Never paste credentials into reports or source files.

## Button appearance and task-view preference

Settings follows the [compact button palette](.impeccable.md#compact-button-appearance): sky-blue secondary actions, blue primary actions, pale-red destructive actions and navy/white selected ranking/default-tab choices. Affected sliced controls use multiplier 2 and thinner shadows. Tag colors and account branding remain exceptions.

Default task tab applies to List. Returning to an active Calendar keeps its month and selection within the same run; restarting starts in List with the saved default tab.
