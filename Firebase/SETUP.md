# Google account and optional aggregate publication

[English](SETUP.md) | [简体中文](SETUP.zh-CN.md)

This guide includes configuration, data boundaries, isolated verification and troubleshooting.

The Windows app uses the system browser, Google OAuth authorization code with S256 PKCE, Firebase Authentication REST and Firestore REST. No Firebase Unity SDK or additional Unity package is required. Firebase's Unity desktop SDK is intended for development workflows, so it is not used here.

This repository does not contain a Firebase project, cloud credentials or an already deployed backend. Until the connection asset is configured, local settings/tasks/progress work and Google sign-in is unavailable.

## Configure the project

1. Create a Firebase project in the Firebase console. Keep Analytics disabled unless separately needed. Record its **Project ID** from Project settings.
2. Register a **Web app** in that project to obtain the Firebase web API key. No Hosting deployment is required. Enable Authentication's **Google** provider and set its support email. Retain Firebase's existing Web client configuration.
3. In the same Google Cloud project, open Google Auth Platform, configure Branding/Audience, and add the Google accounts used during testing as test users while the consent screen is in Testing. Request only `openid` and `email`.
4. Create an OAuth client with application type **Desktop app**. Copy its Client ID. The Windows client opens a randomly assigned `http://127.0.0.1:<port>/catquest-oauth/<random>/` callback. Do not create a web-client redirect or embed a browser. Google supports loopback redirects for desktop clients.
5. Add this Desktop OAuth client ID to the Google identity provider's allowed/external client IDs when required by the Firebase/Identity Platform console. It must belong to the same project or be explicitly allowed. Do not replace the provider's Web client ID with a desktop ID. The credential exchange uses `https://<project-id>.firebaseapp.com` as its request URI. Retain that Firebase-provided authorized domain.
6. Create a **Cloud Firestore** database with database ID `(default)` in production mode. Select the project's intended region. This implementation stores one document at `leaderboard/<Firebase UID>`.
7. In Unity, select `Assets/Resources/AccountConnectionConfig.asset` (or create it using **Create > Cat Quest > Account Connection** under `Assets/Resources`). Fill `firebaseProjectId`, `firebaseApiKey`, and `googleDesktopClientId`. `googleDesktopClientSecret` is optional and only accepts the Desktop client's issued value when Google's client configuration requires it. A desktop client is public: this value cannot be treated as a confidential server credential. **Never put a service-account JSON/private key or Web client secret in the app.**
8. Deploy `Firebase/firestore.rules` and `Firebase/firestore.indexes.json` to the selected project. With Firebase CLI installed and authenticated, run from this directory: `firebase deploy --only firestore:rules,firestore:indexes --project <project-id>`. This is an explicit deployment step. The Unity app and verifiers do not deploy rules. Review the target project before running it.
9. Build a Windows development player. Sign in using a test user, choose the public display name, then explicitly enable sharing. Confirm the document contains exactly the eight approved fields. Close/reopen the app to test secure sign-in restoration. Sign out and confirm the saved credential was removed.

The API key and OAuth client ID identify the project. They are not administrative credentials. Apply supported API restrictions in Google Cloud and test the Unity REST flow after restrictions change. Do not log tokens or OAuth callback URLs. Firebase refresh tokens are stored only in Windows Credential Manager, keyed by project and local save location. Access tokens remain in memory.

## Behavior and privacy

- The entire upload is `playerId`, `playerName`, `bossStage`, `totalTasksCompleted`, `totalFocusMinutes`, `power`, `catLevel`, and a server `updatedAt`. No `PlayerData` serialization is sent. Task titles/descriptions/schedules/tags, goals, AI content and detailed history remain local.
- Login does not upload. First sharing opt-in binds this local save to that Firebase UID. Sign-out preserves the binding and progress. Another account cannot publish that save. This phase has no save transfer or per-account local profiles.
- All gameplay remains local and authoritative. The cloud is a replaceable ranking snapshot, not backup or cloud-save recovery. Rules validate ownership and shape, not the truth of offline progress. This is not a cheat-resistant competitive backend.
- Normal progression reports the highest unlocked Boss using existing victory/level gates. A Boss 3 win reports `3 + current masterStage`. Power uses the existing final equipped stats.
- Task totals count distinct non-revoked completion occurrences, including capped rewards. Focus totals floor the sum of eligible seconds across distinct valid Completed/EndedEarly sessions, falling back to legacy eligible minutes when seconds are absent. Reward caps do not reduce this metric.
- Each account has one snapshot. Updates use Firestore document `updateTime` preconditions. Another device's publication pauses sharing and offers an explicit comparison/replacement. Totals are never automatically summed or merged. Switching equipment or undoing completion can legitimately lower a metric.
- Disabling sharing stops uploads immediately and queues removal. Offline removal stays visibly pending until the linked account signs in and deletion succeeds. A previously publishing device treats a remotely deleted document as a conflict instead of silently recreating it.
- If the first create request loses its response and no document is visible, the client cannot prove that request will never arrive. It performs bounded reads and keeps publication/removal visibly pending. It does not send another create or claim removal. Seeing the exact pending snapshot allows reconciliation. This conservative case can require inspecting the test project/server request state. The approved snapshot schema intentionally contains no extra server consent/tombstone record. Turning off while a request is active waits for its result before removing the document.
- Signed-in players can read/list published leaderboard aggregates. Anonymous reads and all foreign writes are denied. Four composite indexes support the ranking page. Wait until indexes are enabled after deployment. See `../LEADERBOARD.md`.

## Verification

For an explicitly authorized live Windows test, a development player supports `--account-smoke --account-test-dir "<absolute isolated directory>"`. Create an `account-test.marker` file in that directory first. The app then uses `account-test-save.json` and a separate Credential Manager target, opens the authored Account panel, and writes token-free `account-result.json` diagnostics. This mode is disabled in non-development builds. It does not auto-login, opt in, replace a conflicting remote record or delete published data. The browser consent is performed by the user. Commands in `account-command.json` invoke the existing account controller. A `verify` command reads the authenticated Firestore document in-process and exports only status/allowlist/equality checks, never credentials or the response body. Use a fresh directory and a dedicated test account. Remove only the record created by the test before signing out. Keep normal PlayerPrefs backed up/restored by the test launcher. The `--focus-smoke` runner deliberately skips credential restoration and cannot establish real account restart behavior.

The Unity menu **Tools > Cat Quest > Verify Settings Account** (or `Temp/verify-settings-account.request`) runs isolated aggregate, OAuth callback, serialization, ownership, save-failure, upload reconciliation, conflict and removal checks. Output is `Temp/verify-settings-account.result`. Isolated artifacts are under `Library/SettingsAccountVerification`. It never reads real credentials or calls Google/Firebase.

`verify-rules.mjs` runs the authored rules against a local Firestore emulator using the official `@firebase/rules-unit-testing` package. It covers owner create/read/update/delete, anonymous access/foreign writes denial, every required field, extra/private fields, metric/name boundaries, server timestamps, and authenticated ranking queries, deterministic ties and exact own ranks outside the top 50. Both creates and updates are checked for invalid payloads. It refuses non-loopback emulator hosts and projects without the `demo-` prefix. It clears only that isolated emulator database. The run stops after 120 seconds and returns a failing exit code for failed assertions.

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
  if ($LASTEXITCODE -ne 0) { throw 'Firestore rules verification failed. Inspect results/results.json.' }
} finally {
  Pop-Location
}
```

All npm files, emulator logs, `results/results.json`, and JSON/HTML rule coverage stay under ignored `Temp/`. The report records the actual authored rules SHA-256 and run time. No project package manifest is changed. The runner explicitly loads `Firebase/firestore.rules` on each run, in addition to the CLI startup copy. No Firebase login, real project or credentials are needed. Use a free local port if another emulator is already running. Leave the host as `127.0.0.1`. These tests establish local rules behavior, not deployed rules, valid cloud configuration or successful Google OAuth. See the [official emulator rules-testing documentation](https://firebase.google.com/docs/firestore/security/test-rules-emulator).

After configuration, manually verify Windows browser consent/cancel/timeout, secure restart/sign-out, wrong-account refusal, normal upload, offline retry, turning off during an upload, pending removal across restart, another-device conflict/replacement, and stale callbacks after sign-out. Use a dedicated test account/save, never the live player save. Firebase rules can be validated with the Emulator Suite before deployment. Do not treat local mocked transport tests as proof of deployed rules or successful Google sign-in.

References: [Google desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Firebase Auth REST](https://firebase.google.com/docs/reference/rest/auth), [Google provider external client IDs](https://cloud.google.com/identity-platform/docs/web/google), [Firestore REST](https://firebase.google.com/docs/firestore/use-rest-api), [Firestore write preconditions](https://firebase.google.com/docs/firestore/reference/rest/v1/Precondition), [server timestamp transforms](https://firebase.google.com/docs/firestore/reference/rest/v1/Write), [Firebase Unity desktop limitations](https://firebase.google.com/docs/unity/setup#desktop-workflow).

## Console details and troubleshooting

Use the Firebase **Project ID**, not its display name or numeric project number. Registering a Web app supplies REST configuration and does not turn Cat Quest into a website. Hosting is unnecessary. In Authentication, enable Google and retain the provider's existing Web client and `<project-id>.firebaseapp.com` authorized domain. In Google Auth Platform, configure Branding/support contacts, External/Testing as appropriate and the testing accounts. Request only openid/email, not Drive, Calendar or contacts.

The Desktop app client must be in the same project or allowed as an external client. Do not substitute a Web application client or manually register a random loopback URL. Retain a Desktop client's issued secret only if its exchange requires it. Use the Standard Firestore database with ID `(default)` and production rules. The first opt-in creates the leaderboard document, so manual collection creation is unnecessary. Rules can be published in the console, but deploying rules and all four indexes together through the existing Firebase CLI configuration is preferable. Do not rerun firebase init or make the database public to troubleshoot.

On a fresh checkout, copy `Firebase/AccountConnectionConfig.asset.example` to `Assets/Resources/AccountConnectionConfig.asset` and let Unity generate metadata, or use Create > Cat Quest > Account Connection. The asset and metadata are ignored. Keep existing local configuration and rebuild Windows after changes. The existing executable does not automatically read edited Unity assets.

For a live test, use a dedicated save/account or a separate Windows test user with independent local storage and Credential Manager. Copying the executable to another folder does not isolate its save. Focus smoke deliberately bypasses real credential restoration and is unsuitable for account restart testing. Keep Account open during browser login, check Authentication Users after sign-in, then explicitly opt in with a trimmed 1–20 character display name. First opt-in binds the save to the UID. Verify the exact eight fields, aggregate updates, restart, sign-out, then removal using the original account. Offline removal remains pending. This is not cloud-save transfer or backup.

| Symptom | Check |
| --- | --- |
| Google button disabled | Required project ID, API key and Desktop client ID, saved asset and rebuilt executable |
| access_denied | Test-user membership and school/account administrator restrictions |
| redirect_uri_mismatch or invalid client | Desktop app type, correct undeleted ID, not a Web client |
| Browser succeeds but Firebase fails | Enabled provider, matching API key/project, allowed Desktop ID and whether its own secret is required |
| API key rejected | API restrictions allow Authentication REST. Website HTTP referrer restrictions are inappropriate for desktop REST |
| Upload Permission denied | `(default)` database, deployed rules and the save's bound UID |
| Login timeout | Complete within two minutes with Account open and local loopback unblocked |
| Another-device conflict | Compare and explicitly choose replacement in Account, do not bypass preconditions in rules |

Reference for console setup: [Firebase Web registration](https://firebase.google.com/docs/web/setup), [Google provider](https://firebase.google.com/docs/auth/web/google-signin), [rules deployment](https://firebase.google.com/docs/rules/manage-deploy). Live cloud setup must be checked separately from mocks/emulators.
