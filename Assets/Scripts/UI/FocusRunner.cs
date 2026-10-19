using System.Diagnostics;
using UnityEngine;

// Lives on the persistent data manager, so page switches never stop the timer.
public sealed class FocusRunner : MonoBehaviour
{
    private FocusController controller;
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
        controller = new FocusController(GameDataManager.EnsureInstance().Tasks);
        Application.runInBackground = true;
        if (!GetComponent<FocusExitGuard>()) gameObject.AddComponent<FocusExitGuard>();
        lastTime = Now;
    }
    private void Update()
    {
        double now = Now;
        Controller.Advance(now - lastTime);
        lastTime = now;
    }
    private void OnApplicationPause(bool paused)
    {
        if (paused) controller?.Cancel(true);
        lastTime = Now;
    }
    private void OnApplicationQuit() { controller?.Cancel(true); }
}
