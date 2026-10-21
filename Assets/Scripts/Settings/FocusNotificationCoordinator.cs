using System;

public enum FocusNotificationKind { FocusCompleted, RestCompleted }

public interface IFocusNotificationSink
{
    bool TryShow(FocusNotificationKind kind);
}

// Session-scoped presentation only. Notification failures cannot undo a saved Focus result.
public sealed class FocusNotificationCoordinator : IDisposable
{
    readonly FocusController controller;
    readonly Func<bool> enabled;
    readonly IFocusNotificationSink sink;
    string focusSessionId, restSessionId;
    bool disposed;
    public int Attempts { get; private set; }
    public int Accepted { get; private set; }
    public string LastSessionId { get; private set; }
    public FocusNotificationKind LastKind { get; private set; }
    public string LastError { get; private set; }

    public FocusNotificationCoordinator(FocusController controller, Func<bool> enabled, IFocusNotificationSink sink)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        controller.FocusCompleted += OnFocusCompleted;
        controller.RestCompleted += OnRestCompleted;
    }

    void OnFocusCompleted(string sessionId) => Deliver(sessionId, FocusNotificationKind.FocusCompleted, ref focusSessionId);
    void OnRestCompleted(string sessionId) => Deliver(sessionId, FocusNotificationKind.RestCompleted, ref restSessionId);
    void Deliver(string sessionId, FocusNotificationKind kind, ref string seenSessionId)
    {
        if (disposed || string.IsNullOrEmpty(sessionId) || sessionId != controller.SessionId || seenSessionId == sessionId) return;
        // Mark disabled or failed events too: toggling/retrying never replays a stale reminder.
        seenSessionId = sessionId;
        try
        {
            if (!enabled()) return;
            LastSessionId = sessionId; LastKind = kind; LastError = null; Attempts++;
            if (sink.TryShow(kind)) Accepted++;
        }
        catch (Exception)
        {
            LastError = "The Windows reminder could not be displayed.";
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        controller.FocusCompleted -= OnFocusCompleted;
        controller.RestCompleted -= OnRestCompleted;
    }
}
