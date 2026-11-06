using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using CatQuest.Updates;

public enum AppUpdateState { Idle, Checking, Current, Available, Downloading, Verifying, Ready, Installing, Error }

// Only the main process downloads. Installation is delegated to a separate executable.
public sealed class AppUpdateController : MonoBehaviour
{
    public static AppUpdateController Instance { get; private set; }
    public event Action Changed;
    public AppUpdateState State { get; private set; }
    public string Message { get; private set; } = "Check for the latest Cat Quest release.";
    public ReleaseManifest Release { get; private set; }
    public float Progress { get; private set; }
    public string DownloadDirectory => work;
    public bool Busy => State == AppUpdateState.Checking || State == AppUpdateState.Downloading || State == AppUpdateState.Verifying || State == AppUpdateState.Installing;
    public static string InstallDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    string work;
    UnityWebRequest request;
    bool disposed, cancelled;
    public static AppUpdateController Ensure()
    {
        if (Instance) return Instance;
        if (!AppPlatform.WindowsDesktop) return null;
        if (CompanionProtocol.HasArgument("--companion")) return null;
        var go = new GameObject("App Updates"); DontDestroyOnLoad(go);
        return go.AddComponent<AppUpdateController>();
    }
    void Awake() { Instance = this; }
    async void Start()
    {
        // Editor/tests never contact the release service automatically.
        if (Application.isEditor || UnityEngine.Debug.isDebugBuild || CompanionProtocol.HasArgument("--focus-smoke") || AccountBuildSmoke.Enabled || AiBuildSmoke.Enabled) return;
        await Task.Delay(8000);
        if (!disposed && State == AppUpdateState.Idle) Check();
    }
    void Set(AppUpdateState state, string message)
    {
        if (disposed) return;
        State = state; Message = message; Changed?.Invoke();
    }
    public async void Check()
    {
        if (Busy || State == AppUpdateState.Ready) return;
        Set(AppUpdateState.Checking, "Checking for updates...");
        try
        {
            using (var web = UnityWebRequest.Get(UpdateRules.Feed))
            {
                request = web; web.timeout = 20; web.redirectLimit = 5;
                web.SetRequestHeader("User-Agent", "CatQuest/" + Application.version);
                var operation = web.SendWebRequest();
                while (!operation.isDone) { if (disposed) return; if (web.downloadedBytes > 64 * 1024) throw new IOException("Update information is too large."); await Task.Yield(); }
                if (web.responseCode == 404) { Set(AppUpdateState.Current, "No public update package has been published yet."); return; }
                if (web.result != UnityWebRequest.Result.Success) throw new IOException("Could not check for updates. Check your connection and try again.");
                if (web.downloadedBytes > 64 * 1024) throw new IOException("Update information is too large.");
                var release = JsonUtility.FromJson<ReleaseManifest>(web.downloadHandler.text);
                UpdateRules.Validate(release);
                Release = release;
                Set(UpdateRules.IsNewer(release.version, Application.version) ? AppUpdateState.Available : AppUpdateState.Current,
                    UpdateRules.IsNewer(release.version, Application.version) ? "Cat Quest " + release.version + " is available." : "You have the latest version.");
            }
        }
        catch (Exception e) { Set(AppUpdateState.Error, e.Message); }
        finally { request = null; }
    }
    public async void Download()
    {
        if (Busy || Release == null || !UpdateRules.IsNewer(Release.version, Application.version)) return;
        cancelled = false; Progress = 0;
        Set(AppUpdateState.Downloading, "Downloading update...");
        try
        {
            if (Application.isEditor) throw new IOException("Install updates from the Windows application, not the Unity Editor.");
            if (!File.Exists(Path.Combine(InstallDirectory, UpdateRules.InventoryName))) throw new IOException("This build is not packaged for updates. Install a release folder first.");
            work = Path.Combine(Application.temporaryCachePath, "Updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "package.zip");
            using (var web = UnityWebRequest.Get(Release.url))
            {
                request = web; web.timeout = 1800; web.redirectLimit = 5;
                web.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
                var operation = web.SendWebRequest();
                while (!operation.isDone)
                {
                    if (disposed) return;
                    if (web.downloadedBytes > (ulong)Release.size) throw new IOException("Update download exceeds its published size.");
                    Progress = Mathf.Clamp01((float)web.downloadedBytes / Release.size); Changed?.Invoke();
                    await Task.Delay(100);
                }
                if (cancelled) { Set(AppUpdateState.Available, "Download cancelled. You can try again later."); return; }
                if (web.result != UnityWebRequest.Result.Success) throw new IOException("Download failed. Your installed version has not changed. Try again.");
            }
            request = null;
            Set(AppUpdateState.Verifying, "Verifying download...");
            string hash = await Task.Run(() => UpdateRules.Hash(zip));
            if (disposed) return;
            if (new FileInfo(zip).Length != Release.size || !string.Equals(hash, Release.sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update checksum failed. Download the update again.");
            Progress = 1; Set(AppUpdateState.Ready, "Downloaded and verified. Restart when you are ready.");
        }
        catch (Exception e) { Set(AppUpdateState.Error, e.Message); }
        finally { request = null; }
    }
    public void CancelDownload() { if (State == AppUpdateState.Downloading && request != null) { cancelled = true; request.Abort(); } }
    public static string RestartBlockReason()
    {
        if (Application.isEditor) return "Updates can only be installed in the Windows application.";
        var manager = GameDataManager.Instance;
        if (!manager) return "Wait for Cat Quest to finish starting.";
        if (manager.IsSaveReadOnly) return "Resolve the save warning before restarting to update.";
        var runner = manager.GetComponent<FocusRunner>();
        if (runner && (runner.Controller.Phase == FocusPhase.Focusing || runner.Controller.Phase == FocusPhase.Saving || runner.Controller.Phase == FocusPhase.Resting))
            return "Finish Focus, Rest and any pending settlement before restarting.";
        if (manager.PlayerData.battles.Any(b => b.outcome == BattleOutcome.Running)) return "Finish your current battle before restarting.";
        return null;
    }
    public async void Install()
    {
        if (State != AppUpdateState.Ready) return;
        string blocked = RestartBlockReason();
        if (blocked != null) { Message = blocked; Changed?.Invoke(); return; }
        var host = CompanionHost.Instance;
        Process updater = null;
        Set(AppUpdateState.Installing, "Preparing to restart...");
        try
        {
            // Flush pending slider edits while the main view is still available.
            foreach (var slider in FindObjectsByType<SettingsSliderCommit>(FindObjectsInactive.Include, FindObjectsSortMode.None)) slider.Flush();
            if (!GameDataManager.Instance.TrySaveData(out var error)) throw new IOException(error);
            PlayerPrefs.Save();
            if (host)
            {
                host.BeginUpdateShutdown();
                for (int i = 0; i < 100 && host.IsOpen; i++) await Task.Delay(100);
                if (host.IsOpen) throw new IOException("Pet Widget did not close. Please close it and retry.");
            }
            blocked = RestartBlockReason(); if (blocked != null) throw new IOException(blocked);
            string probe = Path.Combine(InstallDirectory, ".update-write-test-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { } File.Delete(probe);
            string helper = Path.Combine(work, "CatQuestUpdater.exe");
            File.Copy(Path.Combine(InstallDirectory, "CatQuestUpdater.exe"), helper, true);
            string ready = Path.Combine(work, "ready"); if (File.Exists(ready)) File.Delete(ready);
            using (var parent = Process.GetCurrentProcess())
                File.WriteAllText(Path.Combine(work, "request.json"), JsonUtility.ToJson(new InstallRequest {
                    directory = InstallDirectory, package = Path.Combine(work, "package.zip"), version = Release.version,
                    sha256 = Release.sha256, size = Release.size, parentPid = parent.Id, parentStartedUtcTicks = parent.StartTime.ToUniversalTime().Ticks,
                    verificationDirectory = CompanionProtocol.HasArgument("--focus-smoke") ? CompanionProtocol.Argument("--smoke-dir") : null }));
            updater = Process.Start(new ProcessStartInfo(helper, "\"" + Path.Combine(work, "request.json") + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work });
            for (int i = 0; i < 100 && !File.Exists(Path.Combine(work, "ready")) && !updater.HasExited; i++) await Task.Delay(100);
            if (!File.Exists(Path.Combine(work, "ready")) || updater.HasExited) throw new IOException("Updater could not start. Your current version is unchanged.");
            Application.Quit();
            await Task.Delay(2000);
            if (!disposed) throw new IOException("Cat Quest could not exit. Close any pending confirmation, then retry.");
        }
        catch (Exception e)
        {
            // Kill only the exact helper we launched, while this process still prevents installation.
            if (updater != null && !updater.HasExited) updater.Kill();
            if (host) host.CancelUpdateShutdown();
            Set(AppUpdateState.Ready, "Could not restart: " + e.Message);
        }
        finally { updater?.Dispose(); }
    }
    void OnDestroy() { disposed = true; request?.Abort(); if (Instance == this) Instance = null; }
    public void PrepareSmokeUpdate(string folder)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!CompanionProtocol.HasArgument("--focus-smoke")) throw new InvalidOperationException("Requires isolated development smoke mode.");
        work = Path.GetFullPath(Path.Combine(folder, "update"));
        var release = JsonUtility.FromJson<ReleaseManifest>(File.ReadAllText(Path.Combine(work, "release.json")));
        UpdateRules.Validate(release);
        if (UpdateRules.Hash(Path.Combine(work,"package.zip")) != release.sha256) throw new IOException("Fixture checksum mismatch.");
        Release=release; Set(AppUpdateState.Ready,"Verified isolated update fixture.");
#else
        throw new InvalidOperationException("Update fixtures are unavailable in release builds.");
#endif
    }
    public static bool InstallationLocked()
    {
        if (!AppPlatform.WindowsDesktop) return false;
        string path = Path.Combine(InstallDirectory, UpdateRules.LockName);
        if (!File.Exists(path)) return false;
        try { using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { } return false; }
        catch (IOException) { return true; }
    }
}
