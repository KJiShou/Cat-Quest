#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

// Never created during ordinary startup. The marker authorizes only this
// isolated development-player benchmark; no production directory is opened.
public sealed class SqlitePerformanceBuildSmoke : MonoBehaviour
{
    public static string ValidateArguments()
    {
        if (Application.isEditor || !Debug.isDebugBuild || !CompanionProtocol.HasArgument("--sqlite-performance")
            || !CompanionProtocol.HasArgument("--focus-smoke") || CompanionProtocol.HasArgument("--companion")
            || CompanionProtocol.HasArgument("--account-smoke") || CompanionProtocol.HasArgument("--ai-smoke")
            || CompanionProtocol.HasArgument("--save-smoke"))
            throw new InvalidOperationException("SQLite performance verification requires an exclusive Development Player smoke run.");
        string argument = CompanionProtocol.Argument("--smoke-dir");
        if (string.IsNullOrWhiteSpace(argument) || !Path.IsPathRooted(argument))
            throw new InvalidOperationException("SQLite performance verification requires an absolute isolated directory.");
        string directory = Path.GetFullPath(argument).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string live = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(directory, live, StringComparison.OrdinalIgnoreCase)
            || directory.StartsWith(live + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || live.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(directory, "sqlite-performance-authorized.txt")))
            throw new InvalidOperationException("SQLite performance authorization marker is missing or the directory overlaps live storage.");
        return directory;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (!CompanionProtocol.HasArgument("--sqlite-performance")) return;
        try {
            string directory = ValidateArguments();
            var root = new GameObject("SQLite performance verification");
            DontDestroyOnLoad(root);
            var runner = root.AddComponent<SqlitePerformanceBuildSmoke>(); runner.directory = directory;
        } catch (Exception error) {
            Debug.LogError("SQLite performance startup rejected: " + error.GetType().Name);
            Application.Quit(1);
        }
    }

    string directory;
    bool finished;
    int originalFrameRate;
    void Awake()
    {
        originalFrameRate = Application.targetFrameRate;
        Application.targetFrameRate = 60;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var probe in FindObjectsByType<FocusBuildSmoke>(FindObjectsInactive.Include, FindObjectsSortMode.None)) probe.enabled = false;
    }
    void Update()
    {
        // GameDataManager changes its rate on focus loss; this opt-in test holds
        // a documented60fps scheduling policy without changing saved settings.
        if (!finished && Application.targetFrameRate != 60) Application.targetFrameRate = 60;
    }
    async void Start()
    {
        string output = Path.Combine(directory, "sqlite-performance-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        string resultPath = Path.Combine(output, "result.txt");
        File.WriteAllText(Path.Combine(directory, "sqlite-performance-run.txt"), output);
        File.WriteAllText(resultPath, DateTime.UtcNow.ToString("O") + " RUNNING native Development Player benchmark\n");
        Exception failure = null;
        GameDataManager manager = null;
        try {
            DateTime readyDeadline = DateTime.UtcNow.AddSeconds(30);
            while (!GameDataManager.Instance || GameDataManager.Instance.Ready == null) {
                if (DateTime.UtcNow > readyDeadline) throw new TimeoutException("Application startup exceeded30 seconds.");
                await Task.Delay(20);
            }
            manager = GameDataManager.Instance;
            var ready = manager.Ready;
            if (await Task.WhenAny(ready, Task.Delay(30000)) != ready || !(await ready).Succeeded)
                throw new InvalidOperationException("Isolated application storage failed to initialize.");
            string expected = Path.GetFullPath(Path.Combine(directory, "test-save.sqlite3"));
            if (!string.Equals(Path.GetFullPath(manager.SavePath), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Application storage does not match the authorized isolated directory.");
            SceneLoaded(default, LoadSceneMode.Single);
            await SqlitePerformanceVerification.Run(output, (ok, message) => {
                if (!ok) throw new Exception(message);
                File.AppendAllText(Path.Combine(output, "checks.txt"), message + "\n");
            });
        } catch (Exception error) { failure = error; }
        try {
            manager = manager ? manager : GameDataManager.Instance;
            if (manager) {
                var drain = manager.PrepareShutdownAsync();
                if (await Task.WhenAny(drain, Task.Delay(10000)) != drain) throw new TimeoutException("Application storage is still draining; player remains open.");
                await drain;
                await manager.CloseAndApproveQuitAsync();
            }
        } catch (Exception error) {
            File.WriteAllText(resultPath, DateTime.UtcNow.ToString("O") + " FAIL shutdown " + error.GetType().Name + ": " + error.Message);
            finished = true; return;
        }
        File.WriteAllText(resultPath, DateTime.UtcNow.ToString("O") + (failure == null ? " PASS" : " FAIL " + failure.GetType().Name + ": " + failure.Message)
            + " | " + Application.platform + " | Unity " + Application.unityVersion + " | Development Player domain benchmark\n");
        finished = true;
        Application.Quit(failure == null ? 0 : 1);
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        Application.targetFrameRate = originalFrameRate;
    }
}
#endif
