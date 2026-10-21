# Leaderboard

The existing sidebar opens an editable `Assets/Prefabs/Leaderboard/LeaderboardContent.prefab` nested in the original Canvas page. Its tabs, row frames and medals come from SimpleCasual's Light `Ranking.prefab`; typography remains Fredoka and buttons reuse `JuicyButton`. No vendor asset or unrelated page is regenerated.

## Ranking behaviour

- Boss Stage, Tasks Completed, Focus Minutes and Power are independent descending rankings.
- Power ties use higher Cat Level, then ascending stable player ID. Other ties use ascending player ID.
- Boss progression 1–3 displays Boss 1–3; 4 onwards displays Master 1 onwards.
- The scrollable list shows the leading 50 published records. A fixed **Your Rank** row shows the signed-in participant's exact position, including outside the first 50. If present in the leading list, the player is also marked **(You)** there.
- Outside the first 50, the reader fetches the player's published record and counts records before its full ordering cursor. All reads use the first query's server `readTime`. It never substitutes unsent local statistics or invents rank zero.
- Opening the page, changing category, pressing Refresh, or a change in account/publication state refreshes the ranking. There is no continuous polling. Closing the page cancels its own requests without cancelling publication.
- Signed-in users can view rankings without participating. Signed-out, empty, unpublished, opted-out, wrong-account and failed-read states are explicit. Account opens the existing Settings account modal.

Tasks, Focus, rewards and progression remain local and authoritative. Only the existing eight approved aggregate fields are read/published. The display name is user-selected; Google names, emails and private productivity content are not ranking fields. This is not a cheat-resistant server-authoritative competition.

## Firebase deployment

The previous owner-only read rules cannot serve this page. Deploy the updated rules and four composite indexes together, using a Firebase administrator login (separate from the game's Google login):

```powershell
cd 'D:\JiShou\Unity\Cat Quest\Firebase'
npx firebase-tools login
npx firebase-tools deploy --only firestore:rules,firestore:indexes --project 'YOUR_FIREBASE_PROJECT_ID'
```

Use the project ID from `AccountConnectionConfig`. Wait for all four indexes to show **Enabled** in Firestore. Rules permit signed-in reads of published leaderboard aggregates, deny anonymous reads, and retain owner-only writes with the strict field allowlist. No service-account key belongs in Unity.

Local compilation/emulator tests do not deploy this configuration. A real ranking read still needs deployed rules/indexes, a signed-in player and optionally a published record. See [Firebase setup](Firebase/SETUP.zh-CN.md).

## Verification

- `Temp/verify-leaderboard-data.request`: isolated ordering, response parsing, top/own/count contracts, failures, opt-out, cancellation and saved prefab bindings. Results and generated REST contracts under `Library/LeaderboardVerification/Data-*`.
- `Temp/verify-leaderboard-play.request`: existing Editor, isolated save, actual reference and page captures at 1920×1080 and 1280×720, categories, own rank 138, own top row, empty/error/opt-out states, rapid switching, close/reopen and Account modal. Restores Game View, start scene and environment.
- `Firebase/verify-rules.mjs`: emulator ownership/privacy validation plus all four top-50 and exact-rank queries. Set `CATQUEST_QUERY_CONTRACTS` to an absolute generated `query-contracts.json` path to additionally execute the compiled C# REST request shapes. These optional REST shape checks use the emulator-only owner token; signed-in/anonymous policy assertions use the official rules test contexts.
- Account regression: `Temp/verify-settings-account.request`.

Emulator and Play Mode fixtures are synthetic and never publish a real player record. Keyboard/human motion review and live Firebase reads must be reported separately from automated checks.

## Windows presentation checks

Ranking row backgrounds use a Pixels Per Unit Multiplier of 2. Medal numbers use glyph-geometry alignment with a medal-face offset; regular positions remain centered. Row icons follow the category: Boss, task checklist, timer and the equipment panel's Power fist. The white Power artwork is tinted to remain readable on white rows.

Use `Tools/Verify-LeaderboardWindows.ps1 -Executable '<development build>' -Run` for isolated, visible Windows UI verification at 1920x1080 and 1280x720. It uses explicit local preview fixtures, checks full logs and Development Console state, exercises screenshots/minimize/restore, and restores PlayerPrefs. It does not authenticate or publish fixtures. `Tools/Start-AccountVerification.ps1` is the reusable interactive launcher.

On Unity 6000.3.10f1, launching the player with PowerShell `-WindowStyle Hidden` and then forcing `ShowWindow` reproduced PresentFrame/WaitForLastPresentation warnings. Normal visible startup avoided them with notifications both on and off. Interactive verification launchers therefore use Normal startup. Do not disable the Development Console or filter errors to hide a presentation failure. The graphics API, Unity version, notification implementation and rendering settings remain unchanged.