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
    [Serializable] public sealed class Sample { public string operation; public double milliseconds, dispatchMilliseconds, commandMainMilliseconds, presentationMainMilliseconds, storageMilliseconds, followingFrameGapMs; public bool focused; }
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
        while (!GameDataManager.Instance || !GameDataManager.Instance.IsReady || !FindFirstObjectByType<MainMenuNavigation>()) yield return null;
        var manager=GameDataManager.Instance;
        Check(Path.GetFullPath(manager.SavePath)==Path.Combine(directory,"test-save.sqlite3"),"Main Player uses the authorized isolated SQLite save");
        report.unity=Application.unityVersion;report.platform=Application.platform.ToString();report.graphics=SystemInfo.graphicsDeviceType.ToString();
        report.development=UnityEngine.Debug.isDebugBuild;report.width=Screen.width;report.height=Screen.height;
        var nav=FindFirstObjectByType<MainMenuNavigation>();Check(nav.ShowPage(MainMenuPage.Tasks),"Authored Tasks page opens");
        var ui=nav.pages[(int)MainMenuPage.Tasks].GetComponent<TaskSystemUI>();
        yield return null;ui.SelectTab(TaskTab.Todo);
        var initial=manager.Service.ReadVerificationSnapshotAsync();while(!initial.IsCompleted)yield return null;
        Check(!initial.IsFaulted&&initial.Result.Succeeded,"SQLite diagnostic snapshot loads");
        report.tasks=initial.Result.Value.tasks.Count;report.history=initial.Result.Value.history.Count;report.saveBytes=new FileInfo(manager.SavePath).Length;
        if(CompanionProtocol.HasArgument("--save-restart")) {
            Check(JsonUtility.ToJson(initial.Result.Value)==File.ReadAllText(Path.Combine(directory,"restart-expected.json")),"Actual restarted process restores every committed field despite changed legacy JSON");
            var verify=VerifyRestartAsync(manager);while(!verify.IsCompleted)yield return null;
            if(verify.IsFaulted)throw verify.Exception.GetBaseException();
            var shot=Capture("restarted-tasks");while(shot.MoveNext())yield return null;
            report.passed=true;Write("RESTART-PASS");Application.Quit();yield break;
        }
        Write("READY");while(!File.Exists(Path.Combine(directory,"go.request")))yield return null;
        var settle=Wait(2);while(settle.MoveNext())yield return null;
        idle=true;settle=Wait(3);while(settle.MoveNext())yield return null;idle=false;
        var capture=Capture("tasks-before");while(capture.MoveNext())yield return null;
        var checks=VerifySaveAsync(manager,ui);while(!checks.IsCompleted)yield return null;
        if(checks.IsFaulted)throw checks.Exception.GetBaseException();
        capture=Capture("save-retry-success");while(capture.MoveNext())yield return null;
        report.passed=true;Write("RESTART-REQUIRED");Application.Quit();
    }
    async System.Threading.Tasks.Task VerifyRestartAsync(GameDataManager manager)
    {
        var task=await manager.Service.UpsertAsync(new QuestTask{title="Restart continued SQLite write"},null);
        Check(task.Succeeded,"Restart accepts a new ordinary command");
        var flush=await manager.FlushAsync();Check(flush.Succeeded,"Restart flushes new SQLite progress");
    }
    async System.Threading.Tasks.Task VerifySaveAsync(GameDataManager manager,TaskSystemUI ui)
    {
        var service=manager.Service;
        var warm=await service.SaveTagAsync(null,"SQLite benchmark",TagColors.DefaultHex);Check(warm.Succeeded,"Warm transactional tag command succeeds");
        string tagId=warm.Value;
        for(int i=0;i<100;i++) {
            var sample=new Sample{operation="IndexedTagCommitWithVisibleTasksRefresh",focused=Application.isFocused};
            var timer=Stopwatch.StartNew();var pending=service.SaveTagAsync(tagId,"SQLite benchmark "+(i%2),TagColors.DefaultHex);
            sample.dispatchMilliseconds=timer.Elapsed.TotalMilliseconds;
            var saved=await pending;sample.milliseconds=timer.Elapsed.TotalMilliseconds;
            sample.commandMainMilliseconds=service.LastCommandMainMilliseconds;sample.storageMilliseconds=service.LastCommandStorageMilliseconds;
            Check(saved.Succeeded,"Measured SQLite commit succeeds #"+i);
            var renderDeadline=DateTime.UtcNow.AddSeconds(10);
            while(ui.RenderedVersion<saved.Version) {
                if(DateTime.UtcNow>renderDeadline)throw new TimeoutException("Visible Tasks refresh did not finish after commit.");
                await System.Threading.Tasks.Task.Yield();
            }
            sample.presentationMainMilliseconds=ui.LastRefreshMainMilliseconds;
            sample.milliseconds=timer.Elapsed.TotalMilliseconds;
            await System.Threading.Tasks.Task.Yield();sample.followingFrameGapMs=frameGap;report.samples.Add(sample);
        }
        double p95=report.samples.Select(s=>s.milliseconds).OrderBy(x=>x).ElementAt(94);
        double mainP95=report.samples.Select(s=>s.commandMainMilliseconds+s.presentationMainMilliseconds).OrderBy(x=>x).ElementAt(94);
        Check(mainP95<=5,"Native command main-thread P95 <= 5ms");
        Check(p95<=200,"Native command end-to-end P95 <= 200ms");
        var created=await service.UpsertAsync(new QuestTask{title="Windows SQLite failure and retry"},null);
        Check(created.Succeeded,"Create an ordinary task through the real service");string taskId=created.Value;
        ui.SelectTab(TaskTab.Todo);ui.SelectTask(taskId);
        var before=await service.ReadVerificationSnapshotAsync();Check(before.Succeeded,"Capture committed state before fault");
        string original=JsonUtility.ToJson(before.Value);
        using(var locked=new SQLite.SQLiteConnection(manager.SavePath)) {
            locked.Execute("BEGIN IMMEDIATE");
            try {
                int framesBeforeLock = Time.frameCount;
                ui.CompleteSelected(taskId);
                var barrier=await manager.FlushAsync();Check(barrier.Succeeded,"Failed command drains without blocking Unity input");
                Check(Time.frameCount-framesBeforeLock>=10,"Unity keeps rendering during the real SQLite lock wait");
                Check(!string.IsNullOrEmpty(service.LastError),"SQLite write lock reports failure");
                var after=await service.ReadVerificationSnapshotAsync();Check(after.Succeeded&&JsonUtility.ToJson(after.Value)==original,"Failed UI completion leaves every committed field and reward unchanged");
                var notice=GameObject.Find("TaskConfirmDialog");
                Check(notice&&notice.transform.Find("Popup/Text_Info").GetComponent<TMPro.TMP_Text>().text.Length>0,"Actual UI presents a visible actionable save failure");
                await System.Threading.Tasks.Task.Delay(400);
                ScreenCapture.CaptureScreenshot(Path.Combine(directory,"save-failure.png"));
                await System.Threading.Tasks.Task.Delay(100);
                notice.transform.Find("Popup/Confirm").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            } finally {locked.Execute("ROLLBACK");}
        }
        ui.CompleteSelected(taskId);Check((await manager.FlushAsync()).Succeeded,"UI retry drains successfully");
        var committed=await service.ReadVerificationSnapshotAsync();Check(committed.Succeeded&&committed.Value.history.Count(h=>h.taskId==taskId&&!h.revoked)==1,"UI retry settles exactly one completion");
        original=JsonUtility.ToJson(committed.Value);
        var duplicate=await service.CompleteAsync(taskId,true);Check(duplicate.Succeeded,"Duplicate command returns its committed receipt");
        var duplicateState=await service.ReadVerificationSnapshotAsync();Check(duplicateState.Succeeded&&JsonUtility.ToJson(duplicateState.Value)==original,"Duplicate command cannot change data or pay again");
        string portable=Path.Combine(directory,"verified.catquestbackup");var exported=await service.TransferBackupAsync(portable,true);Check(exported.Succeeded,"Consistent portable SQLite backup exports");
        using(var backup=new SQLite.SQLiteConnection(portable,SQLite.SQLiteOpenFlags.ReadOnly))Check(backup.ExecuteScalar<string>("PRAGMA integrity_check")=="ok","Exported standalone SQLite backup passes integrity_check");
        File.WriteAllText(Path.Combine(directory,"restart-expected.json"),original);
        // This synthetic source is deliberately obsolete after successful migration.
        // A real restart must keep authoritative SQLite progress instead of reimporting it.
        File.WriteAllText(Path.Combine(directory,"test-save.json"),"{modified isolated legacy source}");
        Check((await manager.FlushAsync()).Succeeded,"Committed SQLite progress flushed before real process restart");
    }
}
#endif
