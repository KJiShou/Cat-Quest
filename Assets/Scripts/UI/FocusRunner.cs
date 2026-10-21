using System.Diagnostics;
using UnityEngine;

// Lives on the persistent data manager, so page switches never stop the timer.
public sealed class FocusRunner : MonoBehaviour
{
    private FocusController controller;
    private TaskService service;
    private WindowsFocusNotificationSink notificationSink;
    private FocusNotificationCoordinator notifications;
    private bool notificationsEnabled;
    public int NotificationAttempts => notifications?.Attempts ?? 0;
    public int NotificationsAccepted => notifications?.Accepted ?? 0;
    public int NotificationsShown => notificationSink?.Shown ?? 0;
    public int NotificationActivations => notificationSink?.Activations ?? 0;
    public bool NotificationsRegistered => notificationSink?.Registered ?? false;
    public string LastNotificationSessionId => notifications?.LastSessionId;
    public string LastNotificationKind => notifications?.LastSessionId == null ? "" : notifications.LastKind.ToString();
    public string NotificationError => notifications?.LastError ?? notificationSink?.LastError;
    public FocusController Controller
    {
        get { Initialize(); return controller; }
    }
    private double lastTime;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public static FocusRunner EnsureInstance()
    {
        var manager = GameDataManager.EnsureInstance();
        return manager.GetComponent<FocusRunner>() ?? manager.gameObject.AddComponent<FocusRunner>();
    }
    private void Awake() => Initialize();
    private void OnEnable() => Initialize();
    private void Initialize()
    {
        if (controller != null) return;
        service = GameDataManager.EnsureInstance().Tasks;
        controller = new FocusController(service);
        if (!CompanionProtocol.HasArgument("--companion"))
        {
            notificationSink = new WindowsFocusNotificationSink();
            notifications = new FocusNotificationCoordinator(controller, () => notificationsEnabled, notificationSink);
            service.Changed += RefreshNotificationSettings;
            RefreshNotificationSettings();
        }
        Application.runInBackground = true;
        if (!GetComponent<FocusExitGuard>()) gameObject.AddComponent<FocusExitGuard>();
        lastTime = Now;
    }
    private void Update()
    {
        double now = Now;
        Controller.Advance(now - lastTime);
        lastTime = now;
        notificationSink?.Tick();
    }
    private void RefreshNotificationSettings()
    {
        notificationsEnabled = service.Settings.notificationsEnabled;
        notificationSink?.SetEnabled(notificationsEnabled);
    }
    private void OnApplicationPause(bool paused)
    {
        if (paused) controller?.Cancel(true);
        lastTime = Now;
    }
    private void OnApplicationQuit() { controller?.Cancel(true); DisposeNotifications(); }
    private void OnDestroy() { DisposeNotifications(); }
    private void DisposeNotifications()
    {
        if (service != null) service.Changed -= RefreshNotificationSettings;
        notifications?.Dispose(); notificationSink?.Dispose();
    }
}
