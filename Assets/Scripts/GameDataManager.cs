using System.IO;
using UnityEngine;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }
    public PlayerData PlayerData => store.Data;
    public TaskService Tasks { get; private set; }
    public string SavePath => store.Path;
    public string LoadNotice => store.LoadNotice;
    private PlayerDataStore store;

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
    private void Initialize()
    {
        if (store != null) return;
        Instance = this;
        string path = Path.Combine(Application.persistentDataPath, "catquest-data.json");
        // Explicit standalone smoke-test mode always uses an isolated verification store.
        if (CompanionProtocol.HasArgument("--focus-smoke"))
            path = Path.Combine(CompanionProtocol.Argument("--smoke-dir"), "test-save.json");
        if (AccountBuildSmoke.Enabled) path = AccountBuildSmoke.IsolatedSavePath;
#if UNITY_EDITOR
        // Editor integration tests use a disposable store, never the player's save.
        string testPath = System.Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        if (!string.IsNullOrEmpty(testPath)) path = testPath;
#endif
        store = new PlayerDataStore(path);
        Tasks = new TaskService(store, new SystemTaskClock());
        if (!CompanionProtocol.HasArgument("--companion"))
        {
            SettingsAudio.EnsureInstance();
            AccountController.EnsureInstance();
        }
    }
    public void SetUsername(string username)
    {
        var candidate = PlayerDataStore.Copy(PlayerData);
        candidate.username = username;
        if (!store.TrySave(candidate, out var error)) throw new IOException(error);
    }
    public string GetUsername() => PlayerData.username;
    public bool HasUsername() => !string.IsNullOrWhiteSpace(PlayerData.username);
    public void SaveData()
    {
        if (!store.TrySave(PlayerDataStore.Copy(PlayerData), out var error)) Debug.LogError(error);
    }
    [ContextMenu("Delete Save Data")]
    public void DeleteSaveData()
    {
        foreach (string suffix in new[] { "", ".bak", ".tmp" })
            if (File.Exists(SavePath + suffix)) File.Delete(SavePath + suffix);
        store = new PlayerDataStore(SavePath);
        Tasks = new TaskService(store, new SystemTaskClock());
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
}
