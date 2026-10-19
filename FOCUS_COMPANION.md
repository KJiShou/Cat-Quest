# Focus page and Windows companion

Open `Assets/Scenes/CharacterHome.unity` and select Focus. Choose Focus and Rest durations with whole-minute sliders (each 1–60 minutes, defaults 25/5), and an optional incomplete Daily/Todo quest. Start Focus is inside the lake training view. End Focus Early saves earned progress and starts Rest; Cancel discards this session. The quest itself remains editable/completable in Tasks.

The training scene fills the main Focus background with rounded clipping. The page heading and Return to Dashboard button are hidden; use the shared sidebar to navigate. Focus and Rest each have a floating white duration card. The sky shows only the countdown. The quest field opens a dropdown and filters eligible incomplete quests by title as you type (case-insensitive); choosing a quest closes the list, and No quest allows free focus. Clicking outside or pressing Escape closes the dropdown. Reward tiles reuse the blue Fish/EXP boxes from Task Details, plus a matching Boss Ticket tile, with numeric previews and settled session values.

Enable Pet Widget to open a 420×300 always-on-top companion. The rounded window uses a light title bar with the player name centered and Pin, Minimize and Close buttons. Close is copied from SimpleCasual Lobby_Popup_User_Info; Pin/Unpin and Minimize use Lucide icons (license in Assets/Generated/Icons/WindowControls). Pin toggles always-on-top and remains unchanged when resizing. Minimize exposes a taskbar button for restoration without ending the Focus session. The name is published by the main program; the companion does not read the player save. Both the Unity UI mask and native Windows window region have 16-pixel rounded corners, refreshed when resized. The game view fills all space below the bar; the timer overlays the sky and the current session action overlays the water. No quest, reward, preset details or hover tooltips are shown.

At every size, the session action stays hidden while the pointer is outside. Entering the window fades the action in over roughly 0.32 seconds; leaving fades it out over roughly 0.24 seconds. Start, End Focus Early, Finish Rest and Retry Save use the same fade behavior. Animation framing stays fixed during the transition. Use the title bar to move the window, or any of its four edges/eight edge-and-corner hit regions to resize with standard Windows sizing cursors (minimum 240×180). Close hides the native window immediately, then exits the companion process while Unity cleans up. Focus remains running in the main app.

## Build and rebuild

Focus and Rest sliders reuse the Quantity row from SimpleCasual Light/Material_Level_Up, including its blue fill, triangle markers and cyan plus/minus artwork. Drag or click the bar to choose whole minutes; plus/minus changes one minute and disables at 1/60. Settings lock after Focus starts. Generated UI preserves this styling when rebuilt.

- `Tools > Cat Quest > Build Companion Window` updates only the companion prefab, preserving the main Focus page and Dashboard.
- `Tools > Cat Quest > Build Focus and Companion UI` regenerates the serialized page, companion prefab and bootstrap scene using the existing SimpleCasual font, panel artwork and icons.
- Build Windows x64 with the enabled scenes, starting with CatQuestBootstrap, then IntroStory and CharacterHome. The companion is launched from the same executable with a separate startup argument; distribute the entire Windows build folder together.
- The generated executable is `Builds/Windows/CatQuest.exe`. Editor Pet Widget previews use that build, with the Editor remaining the session authority.

## State and persistence

FocusController is the only live timer owner. FocusSessionOptions carries the selected durations and optional quest context. FocusSession receipts additionally store selected durations and the quest title/occurrence snapshot. Legacy receipts normalize missing durations to 25/5; existing balances are unchanged.

CompanionHost publishes an atomic companion-session.json with a heartbeat in a unique temporary directory per main process. Companion commands carry the owner, session and unique request ID; the host rejects stale and duplicate requests. The companion never creates GameDataManager or writes player saves. After three seconds without a heartbeat it freezes its last timer display, disables session controls and switches training to Idle.

Main-page and companion animation use the same display-only IFocusPresentation interface. Rewards remain 1 EXP per full eligible minute, 2 Fish Coins per full 5 eligible minutes and 1 Boss Ticket per full 25 valid minutes, with the existing daily limits of 120 reward minutes and 4 tickets. Rest starts only after successful settlement. OS sleep or a gap greater than ten seconds invalidates an unverifiable active session.

## Verification

`Tools > Cat Quest > Verify Rewards and Focus` covers existing rewards and Dashboard behavior. FocusImplementationVerification additionally checks boundary/custom slider-duration combinations, save failure/retry, early end, cancellation, duplicate settlement, quest snapshots, invalid presets and legacy data. The standalone smoke harness only runs with explicit --focus-smoke and --smoke-dir arguments and uses an isolated test save.

Windows integration artifacts, screenshots, checks and logs are in Library/FocusCompanionVerification. The test uses actual separate Windows processes and native controls to verify topmost behavior, hover, drag/resize, minimized timing and session synchronization.


`Temp/verify-focus-layout.request` runs the optional FocusLayoutVerification check against an isolated save. It checks background containment, hidden page header/navigation, slider bindings, searchable quest selection, session setting locks, Rest and reward display, and captures 1920x1080 and 1280x720 previews in `Library/FocusLayoutVerification`.


### Companion shutdown cleanup

Windows companions are launched with `-force-d3d11`. On the current Unity 6000.3.10f1 build, D3D12Core.dll crashes with 0xc0000005 during tool-window shutdown; D3D11 exits normally. This argument applies only to the child process, not the main application's graphics settings.

`SpriteSkinShutdown` registers the 2D Animation 13.0.4 fallback-buffer cleanup on `Application.quitting`. The package allocates a static ComputeBuffer after scene load even without SpriteSkin instances, but only releases it through deformation-system cleanup. Our sprite-sequence companion does not create that owner. The workaround calls the package's existing idempotent `ClearFallbackBuffer` method; `Assets/Scripts/UI/link.xml` preserves that internal method in stripped builds. Recheck and remove the workaround when upgrading to a package that cleans up the fallback buffer itself.
