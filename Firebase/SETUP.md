# Google account and optional aggregate publication

Step-by-step Chinese guide: [Google 登录与 Firebase 配置](SETUP.zh-CN.md).

The Windows app uses the system browser, Google OAuth authorization code with S256 PKCE, Firebase Authentication REST and Firestore REST. No Firebase Unity SDK or additional Unity package is required. Firebase's Unity desktop SDK is intended for development workflows, so it is not used here.

This repository does not contain a Firebase project, cloud credentials or an already deployed backend. Until the connection asset is configured, local settings/tasks/progress work and Google sign-in is unavailable.

## Configure the project

1. Create a Firebase project in the Firebase console. Keep Analytics disabled unless separately needed. Record its **Project ID** from Project settings.
2. Register a **Web app** in that project to obtain the Firebase web API key. No Hosting deployment is required. Enable Authentication's **Google** provider and set its support email. Retain Firebase's existing Web client configuration.
3. In the same Google Cloud project, open Google Auth Platform, configure Branding/Audience, and add the Google accounts used during testing as test users while the consent screen is in Testing. Request only `openid` and `email`.
4. Create an OAuth client with application type **Desktop app**. Copy its Client ID. The Windows client opens a randomly assigned `http://127.0.0.1:<port>/catquest-oauth/<random>/` callback; do not create a web-client redirect or embed a browser. Google supports loopback redirects for desktop clients.
5. Add this Desktop OAuth client ID to the Google identity provider's allowed/external client IDs when required by the Firebase/Identity Platform console. It must belong to the same project or be explicitly allowed. Do not replace the provider's Web client ID with a desktop ID. The credential exchange uses `https://<project-id>.firebaseapp.com` as its request URI; retain that Firebase-provided authorized domain.
6. Create a **Cloud Firestore** database with database ID `(default)` in production mode. Select the project's intended region. This implementation stores one document at `leaderboard/<Firebase UID>`.
7. In Unity, select `Assets/Resources/AccountConnectionConfig.asset` (or create it using **Create > Cat Quest > Account Connection** under `Assets/Resources`). Fill `firebaseProjectId`, `firebaseApiKey`, and `googleDesktopClientId`. `googleDesktopClientSecret` is optional and only accepts the Desktop client's issued value when Google's client configuration requires it. A desktop client is public: this value cannot be treated as a confidential server credential. **Never put a service-account JSON/private key or Web client secret in the app.**
8. Deploy `Firebase/firestore.rules` and `Firebase/firestore.indexes.json` to the selected project. With Firebase CLI installed and authenticated, run from this directory: `firebase deploy --only firestore:rules,firestore:indexes --project <project-id>`. This is an explicit deployment step; the Unity app and verifiers do not deploy rules. Review the target project before running it.
9. Build a Windows development player. Sign in using a test user, choose the public display name, then explicitly enable sharing. Confirm the document contains exactly the eight approved fields. Close/reopen the app to test secure sign-in restoration; sign out and confirm the saved credential was removed.

The API key and OAuth client ID identify the project; they are not administrative credentials. Apply supported API restrictions in Google Cloud and test the Unity REST flow after restrictions change. Do not log tokens or OAuth callback URLs. Firebase refresh tokens are stored only in Windows Credential Manager, keyed by project and local save location; access tokens remain in memory.

## Behavior and privacy

- The entire upload is `playerId`, `playerName`, `bossStage`, `totalTasksCompleted`, `totalFocusMinutes`, `power`, `catLevel`, and a server `updatedAt`. No `PlayerData` serialization is sent. Task titles/descriptions/schedules/tags, goals, AI content and detailed history remain local.
- Login does not upload. First sharing opt-in binds this local save to that Firebase UID. Sign-out preserves the binding and progress. Another account cannot publish that save; this phase has no save transfer or per-account local profiles.
- All gameplay remains local and authoritative. The cloud is a replaceable ranking snapshot, not backup or cloud-save recovery. Rules validate ownership and shape, not the truth of offline progress; this is not a cheat-resistant competitive backend.
- Normal progression reports the highest unlocked Boss using existing victory/level gates. A Boss 3 win reports `3 + current masterStage`. Power uses the existing final equipped stats.
- Task totals count distinct non-revoked completion occurrences, including capped rewards. Focus totals floor the sum of eligible seconds across distinct valid Completed/EndedEarly sessions, falling back to legacy eligible minutes when seconds are absent. Reward caps do not reduce this metric.
- Each account has one snapshot. Updates use Firestore document `updateTime` preconditions. Another device's publication pauses sharing and offers an explicit comparison/replacement; totals are never automatically summed or merged. Switching equipment or undoing completion can legitimately lower a metric.
- Disabling sharing stops uploads immediately and queues removal. Offline removal stays visibly pending until the linked account signs in and deletion succeeds. A previously publishing device treats a remotely deleted document as a conflict instead of silently recreating it.
- If the first create request loses its response and no document is visible, the client cannot prove that request will never arrive. It performs bounded reads and keeps publication/removal visibly pending; it does not send another create or claim removal. Seeing the exact pending snapshot allows reconciliation. This conservative case can require inspecting the test project/server request state; the approved snapshot schema intentionally contains no extra server consent/tombstone record. Turning off while a request is active waits for its result before removing the document.
- Signed-in players can read/list published leaderboard aggregates. Anonymous reads and all foreign writes are denied. Four composite indexes support the ranking page; wait until indexes are enabled after deployment. See `../LEADERBOARD.md`.

## Verification

For an explicitly authorized live Windows test, a development player supports `--account-smoke --account-test-dir "<absolute isolated directory>"`. Create an `account-test.marker` file in that directory first; the app then uses `account-test-save.json` and a separate Credential Manager target, opens the authored Account panel, and writes token-free `account-result.json` diagnostics. This mode is disabled in non-development builds. It does not auto-login, opt in, replace a conflicting remote record or delete published data. The browser consent is performed by the user; commands in `account-command.json` invoke the existing account controller. A `verify` command reads the authenticated Firestore document in-process and exports only status/allowlist/equality checks, never credentials or the response body. Use a fresh directory and a dedicated test account; remove only the record created by the test before signing out. Keep normal PlayerPrefs backed up/restored by the test launcher. The `--focus-smoke` runner deliberately skips credential restoration and cannot establish real account restart behavior.

The Unity menu **Tools > Cat Quest > Verify Settings Account** (or `Temp/verify-settings-account.request`) runs isolated aggregate, OAuth callback, serialization, ownership, save-failure, upload reconciliation, conflict and removal checks. Output is `Temp/verify-settings-account.result`; isolated artifacts are under `Library/SettingsAccountVerification`. It never reads real credentials or calls Google/Firebase.

`verify-rules.mjs` runs the authored rules against a local Firestore emulator using the official `@firebase/rules-unit-testing` package. It covers owner create/read/update/delete, anonymous access/foreign writes denial, every required field, extra/private fields, metric/name boundaries, server timestamps, and authenticated ranking queries, deterministic ties and exact own ranks outside the top 50. Both creates and updates are checked for invalid payloads. It refuses non-loopback emulator hosts and projects without the `demo-` prefix; it clears only that isolated emulator database. The run stops after 120 seconds and returns a failing exit code for failed assertions.

From the repository root in PowerShell, with Node.js 20+ and Java 21+ installed:

```powershell
$rulesRun = Join-Path (Get-Location) 'Temp/SettingsRulesVerification'
New-Item -ItemType Directory -Path $rulesRun -Force | Out-Null
Copy-Item -LiteralPath 'Firebase/firestore.rules' -Destination (Join-Path $rulesRun 'firestore.rules')
@'
{
  "firestore": { "rules": "firestore.rules" },
  "emulators": {
    "firestore": { "host": "127.0.0.1", "port": 8185 },
    "hub": { "host": "127.0.0.1", "port": 4405 },
    "logging": { "host": "127.0.0.1", "port": 4505 },
    "ui": { "enabled": false },
    "singleProjectMode": true
  }
}
'@ | Set-Content -LiteralPath (Join-Path $rulesRun 'firebase.json') -Encoding utf8
Push-Location $rulesRun
try {
  npm install --no-audit --no-fund --save-exact @firebase/rules-unit-testing@6.0.0 firebase@13.0.0
  if ($LASTEXITCODE -ne 0) { throw 'Test dependency installation failed.' }
  npx --yes firebase-tools@15.34.0 emulators:exec --only firestore --project demo-catquest-settings --config firebase.json 'node ../../Firebase/verify-rules.mjs'
  if ($LASTEXITCODE -ne 0) { throw 'Firestore rules verification failed; inspect results/results.json.' }
} finally {
  Pop-Location
}
```

All npm files, emulator logs, `results/results.json`, and JSON/HTML rule coverage stay under ignored `Temp/`. The report records the actual authored rules SHA-256 and run time; no project package manifest is changed. The runner explicitly loads `Firebase/firestore.rules` on each run, in addition to the CLI startup copy. No Firebase login, real project or credentials are needed. Use a free local port if another emulator is already running; leave the host as `127.0.0.1`. These tests establish local rules behavior, not deployed rules, valid cloud configuration or successful Google OAuth. See the [official emulator rules-testing documentation](https://firebase.google.com/docs/firestore/security/test-rules-emulator).

After configuration, manually verify Windows browser consent/cancel/timeout, secure restart/sign-out, wrong-account refusal, normal upload, offline retry, turning off during an upload, pending removal across restart, another-device conflict/replacement, and stale callbacks after sign-out. Use a dedicated test account/save, never the live player save. Firebase rules can be validated with the Emulator Suite before deployment; do not treat local mocked transport tests as proof of deployed rules or successful Google sign-in.

References: [Google desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Firebase Auth REST](https://firebase.google.com/docs/reference/rest/auth), [Google provider external client IDs](https://cloud.google.com/identity-platform/docs/web/google), [Firestore REST](https://firebase.google.com/docs/firestore/use-rest-api), [Firestore write preconditions](https://firebase.google.com/docs/firestore/reference/rest/v1/Precondition), [server timestamp transforms](https://firebase.google.com/docs/firestore/reference/rest/v1/Write), [Firebase Unity desktop limitations](https://firebase.google.com/docs/unity/setup#desktop-workflow).
