# SQLite storage implementation and build notes

Windows storage uses `catquest-data.sqlite3` beside the previous JSON file. The JSON is a migration source only; after migration, it is neither read as current progress nor dual-written. Downgrading to an older JSON-only application does not preserve new progress. Keep the migration archive and use a new-version SQLite backup for device transfer.

If a previous startup created SQLite before the old JSON was restored, place the old `catquest-data.json` in the same save folder with Cat Quest closed. On the next startup, a database that still contains only startup defaults is backed up as `catquest-data.sqlite3.before-late-legacy-*.sqlite3` and the named legacy profile is migrated. SQLite saves with their own progress keep their data. The source JSON and migration archive are retained.

## Mailbox storage

SQLite schema 3 adds a mail read and claim table. The upgrade retains a standalone pre-upgrade SQLite snapshot. Portable backups transfer the mail records with the balances, and legacy JSON imports start with no mail records. The separate mail cache files hold public message content for offline reading and never act as claim receipts.

## Dependency and licenses

- Unity: 6000.3.10f1, unchanged.
- Provider: `com.gilzoide.sqlite-net` 1.3.3, pinned in both package manifests to `34340bf93526b5143ac404f13cb5c4fefabaa461`.
- Bundled SQLite amalgamation: 3.53.4. The repository verifier actually reported SQLite 3.53.4 on this machine in `Temp/SQLiteMigrationVerification/20261116-044725-repository/result.txt`. This identifies the loaded library; consult the verification report for current UI/build results and remaining gates.
- Provider and sqlite-net use MIT licenses. SQLite itself is public domain. Exact bundled notices are retained in `SQLITE_THIRD_PARTY_NOTICES.txt`; every Windows build copies this file and update packaging includes it.
- The package's x86_64 native plugin importer enables Windows Editor and Win64. Existing Editor/backend/API settings are unchanged. Windows post-build processing requires `Plugins/x86_64/gilzoide-sqlite-net.dll` and validates its PE machine as AMD64. Other provider architecture folders are preserved.

## Persistence and updates

The repository serializes operations off the Unity thread, enables foreign keys, uses WAL for file databases and `synchronous=FULL`. Session-only WebGL uses an in-memory database. Process termination tests exercise transaction recovery; they do not prove durability through every hardware power failure.

The updater changes only paths in validated old/new inventories, never enumerating arbitrary player files for deletion. Inventories reject save databases, WAL/SHM, migration archives/markers and portable backups, including custom database names. The same protected-path rule guards journal recovery. Packaging filters these files even if accidentally placed inside the build data folder.

New package inventories declare `sqlite: true` and require the Win64 native plugin. Old inventories remain readable so JSON-era installations can update. A new package is rejected at creation if the native DLL is missing; checksums cover the DLL alongside all other packaged files.

## Isolated build invocation

Do not use `Temp/build-focus-windows.request`: that older runner writes `Builds/Windows` and is not a Development Build.

The already-open Editor accepts JSON in `Temp/build-app-release.request` with fields `version`, an absolute **empty** `outputDirectory`, `restoreVersion: true`, `development: true`, and `verificationProductName: "CatQuest SQLite Verification"`. Despite the historical request filename, the development flag now selects `BuildOptions.Development`. Use a fresh output below `Builds/SQLiteMigrationVerification/<timestamp>/Windows`. Read `Temp/build-app-release.result` for the result. This builds only: it does not launch CatQuest, update an installation, publish a release or open a player save.

The Windows smoke driver is `Tools/Verify-SaveWindows.ps1 -Executable <isolated CatQuest.exe> -Fixture <explicit synthetic JSON fixture>`. It launches its own isolated process, tests real SQLite contention/retry and repeats a process restart using SQLite after the synthetic old JSON changes. It refuses to start while that exact verification executable is running. An unrelated live CatQuest process is neither terminated nor modified; the verification build uses a distinct product name and the driver never restores the live product’s registry keys. Execute only through the project's serialized verification workflow.

## Verification status

Packaging rejection/preservation checks are in `Tools/Updater/Verification.cs`; run `Tools/Updater/Verify-Updater.ps1`. Current executed results, performance measurements, retained failures and unverified checks are recorded in [SQLITE_VERIFICATION_REPORT.md](SQLITE_VERIFICATION_REPORT.md). On 2026-11-16 the maintainer authorized the v1.4.0 Windows release after disclosure of the actual-old-release isolation gap and Web packaging limitation. This publication decision does not turn those unexecuted/failed checks into passes.


Updater long paths: ship CatQuestUpdater.exe.config beside the helper, including its copied temporary work directory. The .NET Framework 4.8 config opts into modern path normalization and unblocks MAX_PATH. No registry or OS policy is changed. SafePath containment, traversal, reparse-point and component validation remain enabled; new SQLite inventories require the checksummed config.

Microsoft references: [AppContextSwitchOverrides](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/appcontextswitchoverrides-element), [path normalization compatibility](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/mitigation-path-normalization). Read-only framework Release on this verification machine was 533509 on 2026-11-16. The standalone verifier covers Unicode/space paths longer than 260 characters, interrupted installation rollback and retry; consult current timestamped results before claiming success.

On this machine the two config switches alone still left long Directory.CreateDirectory calls failing. The updater also converts its internal absolute filesystem roots to extended-length Windows paths (including the UNC form); normal executable paths remain unchanged for process identity and launch. The bounded probe succeeded with the extended path and failed without it. SafePath validation remains in place before accessing package-relative paths.
