using System;
using System.Linq;

public enum FocusPhase { Ready, Focusing, Saving, Resting, Finished, Interrupted }

// Owns one live session. Only successful persistent settlement can start Rest.
public sealed class FocusController : IFocusPresentation
{
    public event Action Changed;
    // Reminder consumers observe committed results, never the timer before saving succeeds.
    public event Action<string> FocusCompleted;
    public event Action<string> RestCompleted;
    public FocusSessionOptions Options { get; private set; } = new FocusSessionOptions();
    public string SessionId => sessionId;
    public int FocusSeconds => Options.mode == FocusTimerMode.Stopwatch ? 0 : Options.focusMinutes * 60;
    public int RestSeconds => Options.mode == FocusTimerMode.Pomodoro ? Options.restMinutes * 60 : 0;
    public bool CountsUp => Options.mode == FocusTimerMode.Stopwatch && Phase != FocusPhase.Resting;
    public double DisplaySeconds => CountsUp ? (Phase == FocusPhase.Ready || Phase == FocusPhase.Interrupted ? 0 : ElapsedSeconds) : RemainingSeconds;
    public static int DisplaySecond(double seconds, bool countsUp) => (int)Math.Min(int.MaxValue, Math.Max(0, countsUp ? Math.Floor(seconds) : Math.Ceiling(seconds)));
    public static string FormatTime(double seconds, bool countsUp)
    {
        int value = DisplaySecond(seconds, countsUp);
        return value >= 3600 ? $"{value / 3600}:{value / 60 % 60:00}:{value % 60:00}" : $"{value / 60:00}:{value % 60:00}";
    }
    public bool Configure(FocusSessionOptions options)
    {
        if (options == null || !options.IsValid || Phase == FocusPhase.Focusing || Phase == FocusPhase.Saving || Phase == FocusPhase.Resting) return false;
        Options = options.Copy(); Phase = FocusPhase.Ready; ElapsedSeconds = 0; LastResult = null;
        Message = ""; Changed?.Invoke(); return true;
    }
    public FocusPhase Phase { get; private set; }
    public string Message { get; private set; } = "25 minutes of Focus, then 5 minutes of Rest.";
    public double ElapsedSeconds { get; private set; }
    public double RemainingSeconds => Phase == FocusPhase.Resting ? Math.Max(0, RestSeconds - restElapsed)
        : Phase == FocusPhase.Ready || Phase == FocusPhase.Finished || Phase == FocusPhase.Interrupted ? FocusSeconds
        : Math.Max(0, FocusSeconds - ElapsedSeconds);
    public FocusSession LastResult { get; private set; }
    public int SpectatorFishCount => Phase == FocusPhase.Focusing || Phase == FocusPhase.Saving
        ? service.PreviewFocusFish(ElapsedSeconds)
        : Phase == FocusPhase.Resting || Phase == FocusPhase.Finished ? LastResult?.fishCoins ?? 0 : 0;
    private readonly TaskService service;
    private string sessionId;
    private double restElapsed;
    private bool early, cancel, invalid;
    public FocusController(TaskService service) { this.service = service; }
    public bool Start() => Start(Options);
    public bool Start(FocusSessionOptions options)
    {
        if (Phase == FocusPhase.Focusing || Phase == FocusPhase.Saving || Phase == FocusPhase.Resting) return false;
        if (!service.BeginFocus(options, out sessionId)) { Message = service.LastError; Changed?.Invoke(); return false; }
        Options = options.Copy();
        var receipt = service.Snapshot.focusSessions.First(s => s.id == sessionId);
        Options.questTitle = receipt.questTitle; Options.occurrenceDay = receipt.occurrenceDay;
        Phase = FocusPhase.Focusing; ElapsedSeconds = 0; restElapsed = 0; LastResult = null;
        Message = "Stay focused. Your cat is training with you.";
        Changed?.Invoke(); return true;
    }
    public void Advance(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return;
        if (Phase == FocusPhase.Focusing)
        {
            // A large gap cannot be verified as active time (sleep, paused player loop, debugger).
            if (seconds > 10) { Cancel(true); return; }
            ElapsedSeconds = CountsUp ? ElapsedSeconds + seconds : Math.Min(FocusSeconds, ElapsedSeconds + seconds);
            if (!CountsUp && ElapsedSeconds >= FocusSeconds) Settle(false, false, false);
        }
        else if (Phase == FocusPhase.Resting)
        {
            restElapsed += seconds;
            if (restElapsed >= RestSeconds) FinishRest(true);
        }
    }
    public bool EndEarly() => Phase == FocusPhase.Focusing && Settle(!CountsUp, false, false);
    public bool Cancel(bool invalidate = false) => Phase == FocusPhase.Focusing && Settle(false, true, invalidate);
    private bool Settle(bool endedEarly, bool cancelled, bool invalidated)
    {
        early = endedEarly; cancel = cancelled; invalid = invalidated;
        Phase = FocusPhase.Saving;
        return RetrySettlement();
    }
    public bool RetrySettlement()
    {
        if (Phase != FocusPhase.Saving) return false;
        bool saved = cancel ? service.CancelFocus(sessionId, invalid) : service.FinishFocus(sessionId, ElapsedSeconds, early);
        if (!saved) { Message = service.LastError + " Retry to save this result."; Changed?.Invoke(); return false; }
        LastResult = service.Snapshot.focusSessions.First(s => s.id == sessionId);
        if (cancel)
        {
            Phase = FocusPhase.Interrupted;
            Message = invalid ? "Focus interrupted. No rewards granted." : "Focus cancelled. No rewards granted.";
        }
        else
        {
            Phase = Options.mode == FocusTimerMode.Pomodoro ? FocusPhase.Resting : FocusPhase.Finished; restElapsed = 0;
            Message = $"Focus saved: +{LastResult.experience} EXP, +{LastResult.fishCoins} Fish Coins, +{LastResult.tickets} Ticket."
                + (Phase == FocusPhase.Resting ? " Time to rest." : "");
        }
        string completedId = sessionId;
        bool completedNormally = !cancel && LastResult.status == FocusSessionStatus.Completed;
        Changed?.Invoke();
        if (completedNormally) FocusCompleted?.Invoke(completedId);
        return true;
    }
    public void EndRest() => FinishRest(false);
    private void FinishRest(bool elapsed)
    {
        if (Phase != FocusPhase.Resting) return;
        string completedId = sessionId;
        Phase = FocusPhase.Finished; Message = "Rest finished. Start another Focus or return to Dashboard.";
        Changed?.Invoke();
        if (elapsed) RestCompleted?.Invoke(completedId);
    }
}
