using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Main-process UI boundary. All Unity object work and notifications stay on the main thread.
public sealed class ChatGptClient : MonoBehaviour
{
    public static ChatGptClient Instance { get; private set; }
    public event Action Changed;
    [field: NonSerialized] public ChatGptConnection Connection { get; private set; }
    [field: NonSerialized] public AiChatSession Chat { get; private set; }
    public string Status { get; private set; } = "Connect ChatGPT to chat and plan tasks.";
    [field: NonSerialized] public bool Ready { get; private set; }
    public bool Busy => operation != null;
    public bool SigningIn => browser != null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public string VerificationFailure { get; private set; }
#endif
    public bool Enabled => Ready && Connection != null && !Busy && Connection.Connected;
    Task operation;
    Action success;
    CancellationTokenSource cancellation;
    ChatGptBrowserAuth browser;
    TMPro.TMP_FontAsset chineseFallback;
    IChatGptStorage verificationStorage;
    IChatGptHttp verificationHttp;

    public static ChatGptClient EnsureInstance()
    {
        if (CompanionProtocol.HasArgument("--companion")) return null;
        if (Instance) return Instance;
        var existing = FindFirstObjectByType<ChatGptClient>();
        if (existing) { existing.Initialize(); return existing; }
        return new GameObject("ChatGptClient").AddComponent<ChatGptClient>();
    }
    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        Initialize();
    }
    void OnEnable()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Initialize();
    }
    void Initialize()
    {
        if (Connection != null) return;
        Instance = this; Ready = false;
        // Missing Chinese glyphs fall back at runtime; primary authored fonts stay unchanged.
        if (!chineseFallback) {
            chineseFallback = TMPro.TMP_FontAsset.CreateFontAsset("Microsoft YaHei", "Regular");
            if (chineseFallback) TMPro.TMP_Settings.fallbackFontAssets.Add(chineseFallback);
        }
        string directory = Path.Combine(Application.persistentDataPath, "ai");
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY");
        string testSave = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        if (!string.IsNullOrEmpty(isolated)) directory = isolated;
        else if (!string.IsNullOrEmpty(testSave)) directory = Path.Combine(Path.GetDirectoryName(testSave), "ai");
        if (AiBuildSmoke.Enabled) directory = Path.Combine(AiBuildSmoke.DirectoryPath, "ai");
        Connection = new ChatGptConnection(verificationStorage ?? new ChatGptStorage(directory), verificationHttp ?? new ChatGptHttp());
        Chat = new AiChatSession(Connection, verificationStorage ?? new ChatGptStorage(directory), GameDataManager.EnsureInstance().Tasks);

        StartOperation(Connection.Initialize(), () => { Chat.Load(); Ready = true; Status = Connection.Connected ? "ChatGPT connected. Open AI settings to load models." : "Connect ChatGPT to chat and plan tasks."; });
    }
    void StartOperation(Task task, Action completed = null)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        VerificationFailure = null;
#endif
        operation = task; success = completed; Changed?.Invoke();
        task.ContinueWith(t => { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    void Update()
    {
        if (operation != null && operation.IsCompleted)
        {
            var done = operation; operation = null;
            if (done.IsFaulted) {
                var error = done.Exception.GetBaseException();
                Status = SafeError(error);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Diagnostic type/frames only: exception messages can contain private server data.
                VerificationFailure = error.GetType().FullName + "\n" + error.StackTrace;
#endif
            }
            else if (done.IsCanceled) Status = "ChatGPT sign-in was cancelled. Local tasks remain available.";
            else success?.Invoke();
            success = null;
            browser?.Dispose(); browser = null; cancellation?.Dispose(); cancellation = null;
            Changed?.Invoke();
        }
        if (Chat != null && Chat.Tick()) Changed?.Invoke();
    }
    public void Connect()
    {
        if (!Ready || Busy || Connection.Connected) return;
        Chat?.Stop(); cancellation = new CancellationTokenSource();
        try
        {
            browser = new ChatGptBrowserAuth(Connection.Host, Connection.ClientId);
            Status = "Finish authorization in your browser within two minutes. You can cancel or keep using tasks.";
            Application.OpenURL(browser.AuthorizationUrl);
            StartOperation(SignIn(cancellation.Token), () => Status = "Connected. Open Chat to plan your tasks.");
        }
        catch (Exception error) { Status = SafeError(error); browser?.Dispose(); browser = null; cancellation?.Dispose(); cancellation = null; Changed?.Invoke(); }
    }
    async Task SignIn(CancellationToken token)
    {
        await Connection.CompleteSignIn(browser, token).ConfigureAwait(false);
        await Connection.LoadModels(token).ConfigureAwait(false);
    }
    public void CancelSignIn() { if (browser != null) { cancellation?.Cancel(); browser.Dispose(); } }
    public void LoadModels()
    {
        if (!Ready || Busy || !Connection.Connected) return;
        cancellation = new CancellationTokenSource();
        StartOperation(Connection.LoadModels(cancellation.Token), () => Status = "Using your ChatGPT plan. Manage usage in ChatGPT settings.");
    }
    public void Preferences(string model, string language = null)
    {
        if (!Ready || Busy) return;
        Chat?.Stop(); StartOperation(Connection.SavePreferences(model, language), () => Status = "Chat model updated.");
    }
    public void Disconnect()
    {
        if (!Ready || Busy) return;
        Chat?.Stop(); cancellation = new CancellationTokenSource();
        StartOperation(Connection.Disconnect(cancellation.Token), () => Status = "ChatGPT disconnected. Local tasks and progress are unchanged.");
    }
    public static string SafeError(Exception error) => error is ArgumentException ? error.Message :
        error is OperationCanceledException ? "Request stopped. Local tasks are unchanged." : "AI could not finish. Please retry.";
    void OnDisable()
    {
        if (Instance != this) return;
        cancellation?.Cancel(); browser?.Dispose(); Chat?.Stop(); Connection?.Dispose();
        cancellation?.Dispose(); cancellation = null; browser = null; operation = null; success = null;
        Connection = null; Ready = false;
        if (chineseFallback) {
            TMPro.TMP_Settings.fallbackFontAssets.Remove(chineseFallback);
            Destroy(chineseFallback); chineseFallback = null;
        }
    }
    void OnDestroy() { if (Instance == this) { OnDisable(); Instance = null; } }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void ConfigureForVerification(IChatGptStorage storage, IChatGptHttp http)
    {
        if (Busy) throw new InvalidOperationException("AI operation is active.");
        Chat?.Stop(); Connection.Dispose(); Ready = false;
        verificationStorage = storage; verificationHttp = http;
        Connection = new ChatGptConnection(storage, http);
        Chat = new AiChatSession(Connection, storage, GameDataManager.EnsureInstance().Tasks);
        StartOperation(Connection.Initialize(), () => { Chat.Load(); Ready = true; Status = "Isolated AI verification."; });
    }
#endif
}
