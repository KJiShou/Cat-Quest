# Windows folder updates

[English](UPDATES.md) | [简体中文](UPDATES.zh-CN.md)

Cat Quest checks the public `KJiShou/Cat-Quest` GitHub release feed in the background once per release-player launch. Settings → Updates also checks manually. Network failure never blocks local tasks. Editor, development builds and smoke tests do not check automatically.

Downloads are complete Windows ZIPs, not binary patches. The settings modal can be dismissed while downloading. Installation requires explicit **Restart & update** and is blocked during Focus, pending settlement, Rest, a running battle or a read-only save. It flushes pending settings, saves successfully, asks the owned Companion to close and waits for it before handing off to a standalone updater. It does not cancel Focus or grant rewards. The application and Companion use the same executable.

## Publishing

1. Set Unity Player Settings **Version** to a higher numeric version (for example `1.2.1`). Keep Company Name and Product Name unchanged to retain save/PlayerPrefs locations.
2. Build the Windows x64 player using the existing build command (`Temp/build-focus-windows.request`). The build postprocessor compiles `CatQuestUpdater.exe` using the Windows .NET Framework compiler and records the build version. No additional Unity packages or end-user .NET SDK are needed; the helper uses Windows .NET Framework 4.x.
3. Write release notes to a UTF-8 text file. Package the output:

   ```powershell
   ./Tools/Publish-CatQuestPackage.ps1 -BuildDirectory Builds/Windows -OutputDirectory Builds/Releases/1.2.1 -NotesFile Builds/release-notes.txt
   ```

4. Create a **public, non-prerelease GitHub Release**, tag `v1.2.1`, in `KJiShou/Cat-Quest`. Upload **both** generated files: `CatQuest-1.2.1-win-x64.zip` and `catquest-update.json`. Publish only after both uploads finish, and mark it as the latest release. This tool does not publish or change repository visibility. A private repository's releases are not anonymously accessible; never embed a GitHub token in the player.
5. Distribute this ZIP to first-time users. Existing versions without the updater need this one-time manual installation. Future updates use the same unpacked folder. Test every release from the previous packaged version before publishing.

The stable feed is `https://github.com/KJiShou/Cat-Quest/releases/latest/download/catquest-update.json`. The manifest supplies version, notes, ZIP URL, byte size and SHA-256. Package URLs are restricted to this repository's HTTPS release download paths. Authenticity relies on HTTPS and control of that GitHub release; SHA-256 verifies integrity, not independent publisher identity. No signing key or GitHub credential is stored in the game. Use ordinary user-writable folders; the updater does not request administrator elevation.

## File safety and recovery

Use a short writable installation path such as `%USERPROFILE%\Games\CatQuest`. The .NET Framework helper is subject to legacy Windows path limits, including the extra backup directory depth. A deeply nested installation can fail to update; move the closed application folder to a shorter path and retry. The public-release verification confirmed the original inventory remained intact after a long-path failure.

`catquest-package.json` inventories release-owned files and their hashes. Packaging excludes debug `DoNotShip` folders, symbols and unrelated files. The updater verifies the ZIP and every inventoried file, rejects traversal/duplicate paths and symlink destinations, backs up old program files and removes only obsolete files listed by the prior package. Unexpected user-file collisions stop installation. Player saves, account credentials and PlayerPrefs are not included or migrated.

The helper runs from an isolated cache folder, validates the main process ID/start time and executable path, waits for its exit and refuses installation if another instance from that folder remains. A lock prevents normal startup during replacement. Installing a second update cannot overlap the first.

Backups and a durable journal live beside the installation in `<installation-folder>.update-history/<transaction-id>`. Ordinary installation errors roll back changed files. For a process/power interruption, close all instances and run the cached helper (its location is in the update result/error message):

```powershell
& 'C:\path\to\cached\CatQuestUpdater.exe' --recover 'C:\path\to\CatQuest'
```

Recovery is repeatable and only reverses incomplete transactions. It never downgrades a successfully installed release or restores old save data. Keep the backup until the new version is known to work; backup history and download cache currently require manual cleanup. Recovery can fail while files are locked or the disk is unavailable; logs/backups are retained. A successful installation means files were installed and process startup was requested, not that the entire new application has passed a health check.

## Verification

- `Tools/Updater/Verify-Updater.ps1`: isolated version/path/archive validation, success, duplicate, failure/rollback/retry, user-file conflicts and interrupted journal recovery.
- `Tools/Updater/Verify-Handoff.ps1`: disposable native process owner handshake, helper self-update, replacement and restart; this is not a Unity test.
- `Temp/verify-settings-play.request` containing `app-updates`: existing isolated Play runner, reference/state captures at 1920×1080 and 1280×720, modal focus/dismissal and button motion. Uses fixture states; does not access GitHub.
- `Temp/build-app-updates.request`: sequential development builds 1.2 and 1.2.1; restores the original Player Settings version. Output is recorded in `Temp/build-app-updates.result`.
- `Tools/Verify-AppUpdatesWindows.ps1 -BuildRoot <recorded-build-folder>`: real Unity/Companion processes, isolated save, active-session blocking, save failure/retry, complete local package install and new-version restart. Restores PlayerPrefs. No live save is read or written.

All evidence goes under ignored `Library/`, `Temp/` and `Builds/`. Public GitHub delivery must additionally be checked after the first release assets exist.

For an explicit production output directory, write JSON to `Temp/build-app-release.request` with `version`, absolute `outputDirectory` (empty/new directory), and `restoreVersion`. The existing Editor builds with `BuildOptions.None` and writes `Temp/build-app-release.result`. Set `restoreVersion` to `false` only for the final release version; test baselines should restore it.

`Tools/Verify-PublicUpdate.ps1 -Executable <packaged-player> -OutputDirectory <new-evidence-directory>` checks the actual public feed, downloads and verifies its ZIP, installs it, checks restart and isolated save preservation, and reconnects the Companion. Use an older updater-enabled, non-development baseline. `-Mode Startup` checks the release before publication; `-Mode Current` checks the already-current release against GitHub. All modes use an isolated smoke save and restore PlayerPrefs. Startup automatic checking is suppressed in smoke mode, so the network flow explicitly invokes the normal Check/Download/Install methods.
