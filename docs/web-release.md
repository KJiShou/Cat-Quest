# Browser edition and itch.io

The browser edition shares the existing tasks, progression, equipment, Boss and
Focus rules with the Windows application. It is intended for desktop browsers.
Windows remains the complete desktop experience. [Play the browser edition on
itch.io](https://kjishou.itch.io/cat-quest). Separate [Mac local-test build tools](mac-release.zh-CN.md)
are available; the native Mac edition still needs real-device testing and has no Pet Widget.

## Platform differences

| Feature | Browser edition | Windows |
| --- | --- | --- |
| Local tasks, rewards, equipment, Boss, in-page Focus | Available | Available |
| Pet Widget | Header control hidden; use the Focus page | Native separate window |
| ChatGPT / Google desktop sign-in and online ranking | Unavailable; existing panels explain this | Existing implementation |
| Windows notifications / self-updater | Unavailable | Existing implementation |
| Backup export | Unavailable in the session-only demo | SQLite backup through native file picker |
| Backup import | Unavailable | Existing validated importer |

The Mac browser can use the same Web build, subject to actual browser compatibility
testing. The native Mac test application uses a separate Mono/Apple Silicon build.
The current Companion's Win32 window functions, Windows credentials, DPAPI storage
and updater are not macOS implementations.

## Saves and Focus

- Current source uses an in-memory SQLite database for a single session. Reloading
  starts fresh. It does not migrate older browser JSON, promise cross-session
  persistence or provide backup transfer. Use Windows for lasting progress.
- The hosted itch.io build may be older: v1.4.0 is a Windows release and does not
  publish a new Web package. The local session smoke passed, but the tested Web
  Development `.data` file exceeded the existing 200 MiB upload-file limit.
- The template requires Web Locks and HTTPS (localhost is valid for testing), and
  permits one active Cat Quest document per storage partition. A stale writer is
  also rejected. Closing the browser releases the lock.
- Focus uses the existing monotonic clock and interruption policy. A player-loop
  gap longer than ten seconds invalidates the session without rewards. Keep the
  page active; sleeping, suspending or reloading the page is not rewarded Focus.
  End Early still settles earned time under existing rules. Rest grants no rewards.
- Browser memory and lifecycle limits still apply. The demo is not a backup of a
  Windows save and should not be used for progress that must survive closing it.

## Build

Use Unity **6000.3.10f1**, with its **Web Build Support** module installed. Restart
the Editor after installing the module so its build pipeline is loaded. Save any
authored scene edits, then choose **Tools → Cat Quest → Build itch.io Web Version**.
The build tool uses enabled Build Settings scenes, switches to Web, builds into a
new `Builds/Web-<timestamp>/` directory and writes a sibling `.zip`. It restores the
previous target and the Web settings it changes. It never generates or repairs UI.
Current build status is written to `Temp/build-web.result`.

The build uses gzip plus Unity decompression fallback and disables native Web
threads, so it does not require cross-origin isolation headers. The canvas follows
the containing viewport. Keep `index.html` at the ZIP root. The tool checks itch.io's
default limits: 1,000 files, 500 MiB total, 200 MiB per file and 240-character paths.
It does not upload or publish anything.

For automation in the already-open Editor, write an absolute, empty output path:

```json
{"outputDirectory":"D:/JiShou/Unity/Cat Quest/Builds/Web-review","development":false}
```

Save that JSON as `Temp/build-web.request`. Wait for completion and target restoration
before running another verification or build. Never open another Editor on the same
project while it is already running.

## Upload

1. On itch.io, create/edit the project and select **HTML** as its kind.
2. Upload the generated ZIP and mark it **This file will be played in the browser**.
3. Use **Click to launch in fullscreen**, or a 1280×720 embed with fullscreen enabled.
4. Keep the Windows package as a separate downloadable file on the same page.
5. Preview the actual itch.io embed before publishing. Check storage, refresh/reload,
   backup download, resizing, keyboard input and audio after a user interaction.
   Do not mark the release mobile-friendly without separate mobile testing.

Upload configuration reference: [itch.io HTML5 documentation](https://itch.io/docs/creators/html5).

## Verification

`node Tools/Verify-WebStorage.cjs` exercises the actual JavaScript storage bridge
with isolated storage failures. `Temp/verify-web.request` runs browser-store
transaction regressions plus `EquipmentVerification`'s existing aggregate chain,
with current results in `Library/WebVerification/<run>/result.txt`.

These checks supplement a real Web build and browser interaction. They do not prove
itch.io hosting, Safari/macOS, native Windows behaviour, or visual quality by themselves.
