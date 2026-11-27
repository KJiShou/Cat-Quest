using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }
    public SqlitePlayerService Service { get; private set; }
    public Task<OperationResult<bool>> Ready { get; private set; }
    public bool IsReady => Service != null && Service.IsReady;
    public string SavePath => repository?.DatabasePath;
    // Credential identity keeps its old path across storage migration.
    public string CredentialStoreIdentity { get; private set; }
    public string LoadNotice { get; private set; }
    public bool IsSaveReadOnly => !IsReady;
    public bool IsShuttingDown => preparingShutdown;
    private IPlayerRepository repository;
    private bool quitApproved, quitting;
    private bool preparingShutdown, maintaining;
    private float nextMaintenance;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Legacy verifiers must explicitly opt in; production must never open JSON.
    private PlayerDataStore legacyStore;
    private TaskService legacyTasks;
    public TaskService Tasks {
        get {
            string path = Environment.GetEnvironmentVariable("CATQUEST_LEGACY_TEST_STORE");
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("This verifier requires explicit legacy-store isolation. Runtime code must use Service.");
            if (legacyTasks == null) { legacyStore = new PlayerDataStore(path); legacyTasks = new TaskService(legacyStore, new SystemTaskClock()); }
            return legacyTasks;
        }
    }
    public PlayerData PlayerData { get { _ = Tasks; return legacyStore.Data; } }
#endif

    public static GameDataManager EnsureInstance()
    {
        if (Instance) return Instance;
        var existing = FindFirstObjectByType<GameDataManager>();
        if (existing) { existing.Initialize(); return existing; }
        return new GameObject("GameDataManager").AddComponent<GameDataManager>();
    }
    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Initialize();
        DontDestroyOnLoad(gameObject);
    }
    private async void Start()
    {
        if (Ready == null) return;
        var ready = await Ready;
        if (!this || !ready.Succeeded) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (Instance == this && Service.Settings.preloadPetWidget) CompanionHost.Ensure().WarmUp();
#endif
    }
    private void Initialize()
    {
        if (repository != null) return;
        Instance = this;
        if (CompanionProtocol.HasArgument("--companion")) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (CompanionProtocol.HasArgument("--sqlite-performance")) SqlitePerformanceBuildSmoke.ValidateArguments();
        if (CompanionProtocol.HasArgument("--sqlite-crash")) SqliteCrashBuildSmoke.ValidateArguments();
#endif
        string legacy = Path.Combine(Application.persistentDataPath, "catquest-data.json");
        if (CompanionProtocol.HasArgument("--focus-smoke")) {
            string smokeFolder = CompanionProtocol.Argument("--smoke-dir");
            if (string.IsNullOrWhiteSpace(smokeFolder) || !Path.IsPathRooted(smokeFolder)) throw new InvalidOperationException("Focus verification requires an absolute isolated directory.");
            smokeFolder = Path.GetFullPath(smokeFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string liveFolder = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(smokeFolder, liveFolder, StringComparison.OrdinalIgnoreCase) || smokeFolder.StartsWith(liveFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Focus verification cannot use the live save directory.");
            legacy = Path.Combine(smokeFolder, "test-save.json");
        }
        if (AccountBuildSmoke.Enabled) legacy = AccountBuildSmoke.IsolatedSavePath;
        if (AiBuildSmoke.Enabled) legacy = AiBuildSmoke.IsolatedSavePath;
#if UNITY_EDITOR
        string testPath = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        if (!string.IsNullOrEmpty(testPath)) legacy = testPath;
#endif
        CredentialStoreIdentity = Path.GetFullPath(legacy);
        string database = AppPlatform.Browser ? ":memory:" : Path.ChangeExtension(legacy, ".sqlite3");
        repository = new SqlitePlayerRepository(database, AppPlatform.Browser ? null : legacy);
        Service = new SqlitePlayerService(repository, new SystemTaskClock());
        Service.StorageWarning += OnStorageWarning;
        Ready = InitializeAsync();
        Application.wantsToQuit += WantsToQuit;
    }
    private async Task<OperationResult<bool>> InitializeAsync()
    {
        var result = await Service.InitializeAsync();
        if (!this) return result;
        LoadNotice = result.Message;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (!result.Succeeded && (result.Error == PlayerStorageError.Corrupt || result.Error == PlayerStorageError.Migration || result.Error == PlayerStorageError.InvalidData)
            && !CompanionProtocol.HasArgument("--focus-smoke") && !AccountBuildSmoke.Enabled && !AiBuildSmoke.Enabled) {
            while (this && !result.Succeeded && SaveFileDialog.OfferRecovery(LoadNotice)) {
                string snapshot = SaveFileDialog.Pick(false, true);
                if (string.IsNullOrEmpty(snapshot) || !SaveFileDialog.ConfirmRecovery(snapshot)) break;
                var recovered = await ((SqlitePlayerRepository)repository).RecoverFromSnapshotAsync(snapshot);
                if (!this) return result;
                if (recovered.Succeeded) result = await Service.InitializeAsync();
                else result = new OperationResult<bool> { Error = recovered.Error, Message = recovered.Message };
                LoadNotice = result.Message;
            }
        }
#endif
        if (result.Succeeded) { SettingsAudio.EnsureInstance(); AccountController.EnsureInstance(); }
        return result;
    }
    public async Task<OperationResult<bool>> SetUsernameAsync(string username)
    {
        var ready = await Ready;
        return ready.Succeeded ? await Service.RenamePlayerAsync(username) : ready;
    }
    public string GetUsername() => Service?.PlayerName ?? "";
    public bool HasUsername() => IsReady && !string.IsNullOrWhiteSpace(GetUsername());
    public Task<OperationResult<bool>> FlushAsync() => Service.FlushAsync();
    public Task CloseAsync() => Service.CloseAsync();
    public async Task PrepareShutdownAsync()
    {
        preparingShutdown = true;
        // Failed startup never accepted business commands. Closing its repository
        // must remain possible even though normal Flush correctly rejects writes.
        if (Ready != null && Ready.IsCompleted && (Ready.IsFaulted || Ready.IsCanceled || !Ready.Result.Succeeded)) return;
        if (Service.IsReady)
            foreach (var panel in FindObjectsByType<SettingsPanelUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!await panel.FlushPendingSettingsAsync()) throw new IOException("Settings could not be saved. Please retry.");
        var result = await Service.PrepareShutdownAsync();
        if (!result.Succeeded) throw new IOException(result.Message);
        PlayerPrefs.Save();
    }
    public void ResumeAfterShutdownFailure()
    {
        preparingShutdown = false;
        Service?.ResumeCommandAcceptance();
    }
    public async Task CloseAndApproveQuitAsync(int timeoutMilliseconds = 10000)
    {
        await Service.CloseForShutdownAsync(timeoutMilliseconds);
        if (this) quitApproved = true;
    }
    async void Update()
    {
        if (!IsReady || preparingShutdown || maintaining || Time.unscaledTime < nextMaintenance) return;
        nextMaintenance = Time.unscaledTime + 30f;
        maintaining = true;
        try { await Service.MaintainAsync(); }
        catch (Exception) { /* Existing recovery snapshots remain available; retry on the next idle interval. */ }
        finally { maintaining = false; }
    }
    bool WantsToQuit()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        SqliteLifecycleBuildSmoke.Trace("MANAGER wantsToQuit approved="+quitApproved+" preparing="+preparingShutdown+" quitting="+quitting);
#endif
        if (quitApproved || repository == null) return true;
        var runner = GetComponent<FocusRunner>();
        if (runner && (runner.Controller.Phase == FocusPhase.Focusing || runner.Controller.Phase == FocusPhase.Saving)) {
            var guard=GetComponent<FocusExitGuard>();
            if(guard && guard.isActiveAndEnabled)return true; // Only the installed guard may approve settling Focus.
            ShowCloseError("Finish or cancel the active Focus session before closing.");
            return false;
        }
        if (preparingShutdown) return false;
        if (!quitting) _ = CompleteQuitAsync();
        return false;
    }
    async Task CompleteQuitAsync()
    {
        quitting = true;
        var budget = System.Diagnostics.Stopwatch.StartNew();
        try {
            var prepare = PrepareShutdownAsync();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SqliteLifecycleBuildSmoke.Trace("MANAGER awaiting preparation completed="+prepare.IsCompleted);
#endif
            if (await Task.WhenAny(prepare, Task.Delay(10000)) != prepare) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                SqliteLifecycleBuildSmoke.Trace("MANAGER preparation timed out");
#endif
                ShowCloseError("Saving is still in progress. Please retry closing shortly.");
                _ = ResumeWhenPreparationCompletesAsync(prepare);
                return;
            }
            await prepare;
            await CloseAndApproveQuitAsync(Math.Max(1, 10000 - (int)budget.ElapsedMilliseconds));
            if (!this) return;
            Application.Quit();
        } catch (Exception error) {
            if (this) ShowCloseError(error is TimeoutException ? error.Message : "Could not finish saving. Please retry closing.");
            ResumeAfterShutdownFailure();
        } finally { quitting = false; }
    }
    void ShowCloseError(string message)
    {
        LoadNotice = message;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        SaveFileDialog.ShowStorageNotice(message);
#endif
    }
    void OnStorageWarning(string message)
    {
        LoadNotice = message;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (!CompanionProtocol.HasArgument("--focus-smoke") && !AccountBuildSmoke.Enabled && !AiBuildSmoke.Enabled)
            SaveFileDialog.ShowStorageNotice(message);
#endif
    }
    async Task ResumeWhenPreparationCompletesAsync(Task prepare)
    {
        try { await prepare; } catch (Exception) { }
        if (this) ResumeAfterShutdownFailure();
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void SetUsername(string username) { var candidate = PlayerDataStore.Copy(PlayerData); candidate.username = username; if (!legacyStore.TrySave(candidate, out var error)) throw new IOException(error); }
    public void SaveData() { if (!TrySaveData(out var error)) throw new IOException(error); }
    public bool TrySaveData(out string error) { _ = Tasks; return legacyStore.TrySave(PlayerDataStore.Copy(PlayerData), out error); }
    public void DeleteSaveData() => throw new InvalidOperationException("Use an explicit isolated test fixture; automatic save deletion is disabled.");
#endif
    private void OnApplicationFocus(bool focused)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (Instance == this && !CompanionProtocol.HasArgument("--companion"))
            Application.targetFrameRate = focused ? 60 : 30;
#endif
    }
    private void OnDestroy()
    {
        Application.wantsToQuit -= WantsToQuit;
        if (Service != null) Service.StorageWarning -= OnStorageWarning;
        if (Instance == this) Instance = null;
        if (repository != null) _ = repository.CloseAsync();
    }
}
