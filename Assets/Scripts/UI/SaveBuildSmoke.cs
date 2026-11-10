#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit Development Player probe. Uses the existing isolated --focus-smoke
// store; never registers or changes anything during ordinary startup.
public sealed class SaveBuildSmoke : MonoBehaviour
{
    [Serializable] public sealed class Sample { public string operation; public double milliseconds, followingFrameGapMs; public bool focused; }
    [Serializable] public sealed class Report {
        public string utc, unity, platform, graphics, phase, failure;
        public bool development, passed;
        public int width, height, tasks, history;
        public long saveBytes;
        public List<double> idleFrameGapsMs = new List<double>();
        public List<Sample> samples = new List<Sample>();
        public List<string> checks = new List<string>();
    }
    string directory;
    Report report = new Report();
    IEnumerator flow;
    long previousUpdate;
    double frameGap, deadline;
    bool idle;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        if (!CompanionProtocol.HasArgument("--save-smoke") || !CompanionProtocol.HasArgument("--focus-smoke")) return;
        string folder = CompanionProtocol.Argument("--smoke-dir");
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "save-probe-authorized.txt"))) return;
        PlayerPrefs.SetInt("PetWidgetEnabled", 0); // Native driver restores the prior preferences.
        var root = new GameObject("Isolated save build verification");
        DontDestroyOnLoad(root);
        var probe = root.AddComponent<SaveBuildSmoke>(); probe.directory = Path.GetFullPath(folder);
        SceneManager.sceneLoaded += probe.Loaded;
    }
    void Loaded(Scene scene, LoadSceneMode mode)
    {
        var other = FindFirstObjectByType<FocusBuildSmoke>();
        if (other) other.enabled = false; // Avoid its 5Hz polling/serialization in frame measurements.
    }
    void OnDestroy() { SceneManager.sceneLoaded -= Loaded; }
    void Start() { deadline = Time.realtimeSinceStartupAsDouble + 180; flow = Run(); }
    void Update()
    {
        long now = Stopwatch.GetTimestamp();
        frameGap = previousUpdate == 0 ? 0 : (now - previousUpdate) * 1000d / Stopwatch.Frequency;
        previousUpdate = now;
        if (idle && frameGap > 0) report.idleFrameGapsMs.Add(frameGap);
        if (flow == null) return;
        try {
            if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Save Player verification timed out");
            if (!flow.MoveNext()) { flow = null; }
        } catch (Exception error) {
            (flow as IDisposable)?.Dispose(); flow = null; report.failure = error.ToString(); report.passed = false;
            Write("FAIL"); Application.Quit(1);
        }
    }
    void Check(bool value, string message) { if (!value) throw new Exception(message); report.checks.Add(message); }
    void Write(string phase) { report.phase = phase; report.utc = DateTime.UtcNow.ToString("O"); File.WriteAllText(Path.Combine(directory, "save-player-result.json"), JsonUtility.ToJson(report, true)); }
    IEnumerator Wait(double seconds) { double until = Time.realtimeSinceStartupAsDouble + seconds; while (Time.realtimeSinceStartupAsDouble < until) yield return null; }
    IEnumerator Measure(string name, Action action)
    {
        var pause = Wait(.35); while (pause.MoveNext()) yield return null;
        var sample = new Sample { operation = name, focused = Application.isFocused };
        var timer = Stopwatch.StartNew(); action(); timer.Stop(); sample.milliseconds = timer.Elapsed.TotalMilliseconds;
        yield return null;
        sample.followingFrameGapMs = frameGap; report.samples.Add(sample);
    }
    IEnumerator Capture(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, name + ".png"));
        var pause = Wait(.4); while (pause.MoveNext()) yield return null;
    }
    IEnumerator Run()
    {
        while (!GameDataManager.Instance || !FindFirstObjectByType<MainMenuNavigation>()) yield return null;
        var manager = GameDataManager.Instance; var service = manager.Tasks;
        Check(Path.GetFullPath(manager.SavePath) == Path.Combine(directory, "test-save.json"), "Main Player uses the authorized isolated save");
        report.unity = Application.unityVersion; report.platform = Application.platform.ToString(); report.graphics = SystemInfo.graphicsDeviceType.ToString();
        report.development = UnityEngine.Debug.isDebugBuild; report.width = Screen.width; report.height = Screen.height;
        report.tasks = manager.PlayerData.tasks.Count; report.history = manager.PlayerData.history.Count; report.saveBytes = new FileInfo(manager.SavePath).Length;
        var nav = FindFirstObjectByType<MainMenuNavigation>(); Check(nav.ShowPage(MainMenuPage.Tasks), "Authored Tasks page opens");
        var ui = nav.pages[(int)MainMenuPage.Tasks].GetComponent<TaskSystemUI>(); ui.SelectTab(TaskTab.Todo);
        yield return null; // Let the page's Start restore its initial tab before selecting the test view.
        ui.SelectTab(TaskTab.Todo);
        if (CompanionProtocol.HasArgument("--save-recovery")) {
            var startup = Wait(3); while (startup.MoveNext()) yield return null;
            Check(!string.IsNullOrEmpty(manager.LoadNotice), "Restart reports recovery from backup");
            Check(JsonUtility.ToJson(manager.PlayerData) == File.ReadAllText(Path.Combine(directory, "recovery-expected.json")), "Actual restarted process restores the complete expected backup");
            string backup = File.ReadAllText(manager.SavePath + ".bak");
            Check(manager.TrySaveData(out var recoveryError), "Recovered Player saves successfully: " + recoveryError);
            Check(File.ReadAllText(manager.SavePath + ".bak") == backup && Directory.GetFiles(directory, "test-save.json.corrupt.*").Length > 0,
                "Recovery preserves the valid backup and archives the corrupt primary");
            Check(JsonUtility.ToJson(new PlayerDataStore(manager.SavePath).Data) == JsonUtility.ToJson(manager.PlayerData), "Recovered save reloads fully");
            var shot = Capture("recovered-tasks"); while (shot.MoveNext()) yield return null;
            var recoveryNotice = GameObject.Find("TaskConfirmDialog");
            Check(recoveryNotice && recoveryNotice.transform.Find("Popup/Text_Info").GetComponent<TMPro.TMP_Text>().text.Contains("backup"), "Recovery notice is visible in the restarted Player");
            recoveryNotice.transform.Find("Popup/Confirm").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            startup = Wait(1); while (startup.MoveNext()) yield return null;
            shot = Capture("recovered-tasks-dismissed"); while (shot.MoveNext()) yield return null;
            report.passed = true; Write("RECOVERY-PASS"); Application.Quit(); yield break;
        }
        Write("READY"); while (!File.Exists(Path.Combine(directory, "go.request"))) yield return null;
        var settle = Wait(2); while (settle.MoveNext()) yield return null;
        idle = true; settle = Wait(3); while (settle.MoveNext()) yield return null; idle = false;
        var capture = Capture("tasks-before"); while (capture.MoveNext()) yield return null;
        Check(manager.TrySaveData(out var warmError), "Warm save succeeds: " + warmError);
        for (int i = 0; i < 5; i++) {
            var measure = Measure("SnapshotAndSave", () => Check(manager.TrySaveData(out var error), "Measured save succeeds: " + error));
            while (measure.MoveNext()) yield return null;
        }
        string tagId = manager.PlayerData.tags[0].id;
        for (int i = 0; i < 5; i++) {
            var measure = Measure("CommitWithVisibleTasksRefresh", () => Check(service.SaveTag(tagId, "Player save check", TagColors.DefaultHex, out _), "Measured commit succeeds: " + service.LastError));
            while (measure.MoveNext()) yield return null;
        }
        // Profile stages after the end-to-end samples so instrumentation does not
        // warm their caches or introduce extra allocations into the comparison.
        var candidate = PlayerDataStore.Copy(manager.PlayerData);
        string json = JsonUtility.ToJson(candidate);
        byte[] encoded = System.Text.Encoding.UTF8.GetBytes(json);
        object sink = null;
        var hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var store = typeof(GameDataManager).GetField("store", hidden).GetValue(manager);
        var validate = typeof(PlayerDataStore).GetMethod("ValidateFiles", hidden);
        var compress = typeof(PlayerDataStore).GetMethod("Compress", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        for (int i = 0; i < 3; i++) {
            var stages = new Dictionary<string, Action> {
                { "Stage.Copy", () => sink = PlayerDataStore.Copy(manager.PlayerData) },
                { "Stage.Normalize", () => PlayerDataStore.Normalize(candidate) },
                { "Stage.Serialize", () => sink = JsonUtility.ToJson(candidate) },
                { "Stage.Encode", () => sink = System.Text.Encoding.UTF8.GetBytes(json) },
                { "Stage.Validate", () => validate.Invoke(store, new object[] { false, 0, 0 }) },
                { "Stage.Write", () => File.WriteAllBytes(Path.Combine(directory, "stage-write.tmp"), encoded) }
            };
            if (compress != null) stages.Add("Stage.Compress", () => sink = compress.Invoke(null, new object[] { encoded }));
            foreach (var stage in stages) { var measure = Measure(stage.Key, stage.Value); while (measure.MoveNext()) yield return null; }
        }
        GC.KeepAlive(sink);
        Check(service.Upsert(new QuestTask { title = "Windows save / retry check" }, null, out var taskId), "Create an ordinary task through the real service");
        ui.SelectTab(TaskTab.Todo); ui.SelectTask(taskId);
        string original = JsonUtility.ToJson(manager.PlayerData), disk = File.ReadAllText(manager.SavePath), oldBackup = File.ReadAllText(manager.SavePath + ".bak");
        using (var locked = new FileStream(manager.SavePath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            ui.CompleteSelected(taskId);
            Check(!string.IsNullOrEmpty(service.LastError) && JsonUtility.ToJson(manager.PlayerData) == original, "Failed UI completion leaves all in-memory state and rewards unchanged");
            Check(File.ReadAllText(manager.SavePath) == disk && File.ReadAllText(manager.SavePath + ".bak") == oldBackup, "Temporary-write failure preserves both committed files");
            yield return null;
            var notice = GameObject.Find("TaskConfirmDialog");
            Check(notice && notice.transform.Find("Popup/Text_Info").GetComponent<TMPro.TMP_Text>().text.Contains("Could not save"), "Actual UI presents a visible save failure");
            Check(notice.transform.Find("Popup/Text_Info").GetComponent<TMPro.TMP_Text>().text == "Could not save your changes. Please try again.", "Visible failure message offers retry without a technical path");
            capture = Capture("save-failure"); while (capture.MoveNext()) yield return null;
            notice.transform.Find("Popup/Confirm").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        }
        using (var locked = new FileStream(manager.SavePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Check(!service.Complete(taskId, true) && JsonUtility.ToJson(manager.PlayerData) == original, "Persistent replacement lock returns failure without reward settlement");
            Check(File.ReadAllText(manager.SavePath) == disk && File.ReadAllText(manager.SavePath + ".bak") == oldBackup, "Replacement failure preserves primary and backup");
        }
        var completion = Measure("RetryUICompletion", () => ui.CompleteSelected(taskId)); while (completion.MoveNext()) yield return null;
        Check(string.IsNullOrEmpty(service.LastError) && manager.PlayerData.history.Count(h => h.taskId == taskId && !h.revoked) == 1, "UI retry settles exactly one completion");
        capture = Capture("save-retry-success"); while (capture.MoveNext()) yield return null;
        original = JsonUtility.ToJson(manager.PlayerData); ui.CompleteSelected(taskId);
        Check(JsonUtility.ToJson(manager.PlayerData) == original, "Duplicate UI completion cannot pay again");
        Check(JsonUtility.ToJson(new PlayerDataStore(manager.SavePath).Data) == original, "All successful progress reloads from disk");
        capture = Capture("duplicate-completion"); while (capture.MoveNext()) yield return null;
        Check(manager.TrySaveData(out var finalError), "Establish recovery backup after completion: " + finalError);
        var expected = new PlayerDataStore(manager.SavePath + ".bak").Data;
        File.WriteAllText(Path.Combine(directory, "recovery-expected.json"), JsonUtility.ToJson(expected));
        File.WriteAllText(manager.SavePath, "{deliberately damaged isolated primary}");
        report.passed = true; Write("RESTART-REQUIRED"); Application.Quit();
    }
}
#endif
