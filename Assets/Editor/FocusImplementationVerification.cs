using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad] public static class FocusImplementationVerification
{
    sealed class Store : IPlayerDataStore {
        public PlayerData Data { get; private set; }=new PlayerData(); public bool fail;
        public bool TrySave(PlayerData candidate,out string error) { error=fail?"Simulated save failure":null;if(fail)return false;Data=candidate;return true; }
    }
    sealed class Clock : ITaskClock { public DateTime LocalNow=>new DateTime(2026,10,9,12,0,0);public DateTime UtcNow=>LocalNow.AddHours(-8); }
    sealed class NotificationSink : IFocusNotificationSink {
        public readonly List<FocusNotificationKind> calls = new List<FocusNotificationKind>();
        public bool fail, reject;
        public Action observed;
        public bool TryShow(FocusNotificationKind kind) {
            calls.Add(kind); observed?.Invoke();
            if(fail)throw new InvalidOperationException("Simulated notification failure");
            return !reject;
        }
    }
    static FocusImplementationVerification() { EditorApplication.update+=Poll; }
    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/verify-focus-implementation.request"))return;
        File.Delete("Temp/verify-focus-implementation.request");
        try { Run();File.WriteAllText("Temp/verify-focus-implementation.result","PASS: 25 boundary/custom duration combinations, early/cancel, save retry, daily caps, quest snapshot, invalid options, legacy save, duplicate settlement, notification success/retry/disable/failure/reload/manual actions, and existing reward/dashboard checks."); }
        catch(Exception e) { File.WriteAllText("Temp/verify-focus-implementation.result",e.ToString());Debug.LogException(e); }
    }
    static void Check(bool ok,string error) { if(!ok)throw new Exception(error); }
    static TaskService Service(Store store)=>new TaskService(store,new Clock());
    public static void Run()
    {
        foreach(int duration in new[]{1,7,25,37,60}) foreach(int rest in new[]{1,4,5,17,60}) {
            var store=new Store();var service=Service(store);var focus=new FocusController(service);
            Check(focus.Start(new FocusSessionOptions{focusMinutes=duration,restMinutes=rest}),"Cannot start chosen duration");
            for(int i=0;i<duration*60;i++)focus.Advance(1);
            Check(focus.Phase==FocusPhase.Resting && focus.RemainingSeconds==rest*60,"Selected rest not used");
            Check(focus.LastResult.experience==duration && focus.LastResult.fishCoins==duration/5*2 && focus.LastResult.tickets==duration/25,"Duration rewards incorrect");
            Check(!focus.Start() && !focus.Configure(new FocusSessionOptions()),"Active settings changed");
            Check(service.FinishFocus(focus.SessionId,duration*60,false) && store.Data.experience==duration,"Repeated settlement paid twice");
            focus.Advance(rest*60);Check(focus.Phase==FocusPhase.Finished,"Rest did not finish");
        }
        {
            var store=new Store();var service=Service(store);var focus=new FocusController(service);
            Check(!focus.Start(new FocusSessionOptions{focusMinutes=0}),"Below-minimum duration accepted");
            Check(!focus.Start(new FocusSessionOptions{restMinutes=61}),"Above-maximum rest accepted");
            Check(!focus.Start(new FocusSessionOptions{focusMinutes=61}),"Above-maximum focus accepted");
            Check(!focus.Start(new FocusSessionOptions{restMinutes=0}),"Below-minimum rest accepted");
            focus.Start(new FocusSessionOptions{focusMinutes=60,restMinutes=15});for(int i=0;i<359;i++)focus.Advance(1);
            store.fail=true;Check(!focus.EndEarly() && focus.Phase==FocusPhase.Saving && store.Data.experience==0,"Save failure started Rest");
            store.fail=false;Check(focus.RetrySettlement() && focus.LastResult.experience==5 && focus.LastResult.fishCoins==2 && focus.RestSeconds==900,"Early retry rewards wrong");
            focus.EndRest();focus.Start();focus.Advance(5);focus.Cancel();Check(focus.Phase==FocusPhase.Interrupted && store.Data.experience==5,"Cancel paid reward");
        }
        {
            var store=new Store();var service=Service(store);
            service.Upsert(new QuestTask{title="Study",date="2026-10-09"},null,out var questId);
            var focus=new FocusController(service);Check(focus.Start(new FocusSessionOptions{questId=questId}),"Quest cannot be selected");
            store.Data.tasks.Clear();focus.Advance(1);focus.EndEarly();
            Check(focus.LastResult.questTitle=="Study" && focus.LastResult.occurrenceDay=="once" && store.Data.history.Count==0,"Quest snapshot missing or auto-completed");
        }
        {
            var store=new Store();var service=Service(store);
            for(int i=0;i<7;i++){service.BeginFocus(new FocusSessionOptions{focusMinutes=60},out var id);service.FinishFocus(id,3600,false);}
            Check(store.Data.experience==360 && store.Data.fishCoins==144 && store.Data.tickets==4,"Daily caps incorrect");
            var old=JsonUtility.FromJson<PlayerData>("{\"version\":3,\"fishCoins\":99,\"focusSessions\":[{\"id\":\"legacy\",\"fishCoins\":10}]}");PlayerDataStore.Normalize(old);
            Check(old.focusSessions[0].focusDurationSeconds==1500 && old.focusSessions[0].restDurationSeconds==300 && old.fishCoins==99,"Legacy data corrupted");
        }
        VerifyTimerModes();
        VerifyNotifications();
        RewardSystemVerification.Run();
        var canvas=UnityEditor.PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try { var panel=canvas.GetComponentsInChildren<FocusPanelUI>(true).First(p=>!p.quickStart);Check(panel.focusDuration && panel.restDuration && panel.focusDuration.minValue==1 && panel.focusDuration.maxValue==60 && panel.restDuration.minValue==1 && panel.restDuration.maxValue==60 && panel.questList && panel.rewards,"Focus UI unbound");Check(canvas.GetComponentInChildren<PetWidgetToggle>(true),"Header widget toggle unbound"); }
        finally { UnityEditor.PrefabUtility.UnloadPrefabContents(canvas); }
    }

    static void AdvanceFocus(FocusController focus, int seconds = 60)
    {
        for(int i=0;i<seconds;i++)focus.Advance(1);
    }
    static FocusSessionOptions ShortSession() => new FocusSessionOptions { focusMinutes=1, restMinutes=1 };
    static void VerifyNotifications()
    {
        {
            var store=new Store();var service=Service(store);var focus=new FocusController(service);var sink=new NotificationSink();
            bool committedAtDelivery=false;
            sink.observed=()=>committedAtDelivery=focus.Phase==FocusPhase.Resting &&
                store.Data.focusSessions.Any(s=>s.id==focus.SessionId && s.status==FocusSessionStatus.Completed);
            var notifications=new FocusNotificationCoordinator(focus,()=>true,sink);
            focus.Start(ShortSession());AdvanceFocus(focus,59);store.fail=true;focus.Advance(1);
            Check(focus.Phase==FocusPhase.Saving && sink.calls.Count==0,"Unpersisted completion sent a notification");
            Check(!focus.RetrySettlement() && sink.calls.Count==0,"Failed retry sent a notification");
            store.fail=false;Check(focus.RetrySettlement(),"Notification test settlement retry failed");
            Check(committedAtDelivery && sink.calls.SequenceEqual(new[]{FocusNotificationKind.FocusCompleted}),"Focus notification did not follow committed normal completion");
            string sessionId=focus.SessionId;
            Check(notifications.LastSessionId==sessionId && notifications.Accepted==1,"Notification did not retain its session identity");
            focus.RetrySettlement();focus.Advance(0);service.FinishFocus(sessionId,60,false);
            Check(sink.calls.Count==1,"Duplicate settlement repeated a notification");
            focus.Advance(60);focus.Advance(60);focus.EndRest();
            Check(sink.calls.SequenceEqual(new[]{FocusNotificationKind.FocusCompleted,FocusNotificationKind.RestCompleted}),"Natural Rest reminder missing or duplicated");
            notifications.Dispose();notifications.Dispose();focus.Start(ShortSession());AdvanceFocus(focus);focus.Advance(60);
            Check(sink.calls.Count==2,"Disposed notification coordinator kept listeners");
        }
        {
            var store=new Store();var focus=new FocusController(Service(store));var sink=new NotificationSink();
            using(var notifications=new FocusNotificationCoordinator(focus,()=>true,sink)) {
                focus.Start(ShortSession());AdvanceFocus(focus,59);focus.EndEarly();
                Check(sink.calls.Count==0 && focus.Phase==FocusPhase.Resting,"End Early sent an immediate reminder or lost Rest");
                focus.Advance(59);Check(sink.calls.Count==0,"Rest reminder arrived before timeout");focus.Advance(1);
                Check(sink.calls.SequenceEqual(new[]{FocusNotificationKind.RestCompleted}),"Natural Rest after End Early did not remind");
                focus.Start(ShortSession());focus.Advance(1);focus.EndEarly();focus.EndRest();focus.Advance(60);
                Check(sink.calls.Count==1,"Manual End Rest sent a reminder");
                focus.Start(ShortSession());focus.Cancel();focus.Advance(60);
                focus.Start(ShortSession());focus.Advance(11);
                Check(focus.Phase==FocusPhase.Interrupted && sink.calls.Count==1,"Cancelled or invalid Focus sent a reminder");
                focus.Start(ShortSession());store.fail=true;focus.Cancel();store.fail=false;focus.RetrySettlement();
                Check(focus.Phase==FocusPhase.Interrupted && sink.calls.Count==1,"Retried cancellation sent a completion reminder");
            }
        }
        {
            var focus=new FocusController(Service(new Store()));var sink=new NotificationSink();bool enabled=false;
            using(var notifications=new FocusNotificationCoordinator(focus,()=>enabled,sink)) {
                focus.Start(ShortSession());AdvanceFocus(focus);focus.Advance(60);enabled=true;
                focus.Configure(ShortSession());focus.Advance(0);
                Check(sink.calls.Count==0,"Enabling notifications replayed an old event");
                focus.Start();AdvanceFocus(focus);enabled=false;focus.Advance(60);enabled=true;focus.Advance(0);
                Check(sink.calls.SequenceEqual(new[]{FocusNotificationKind.FocusCompleted}),"Current notification setting was ignored");
            }
        }
        {
            var store=new Store();var focus=new FocusController(Service(store));var sink=new NotificationSink{fail=true};
            using(var notifications=new FocusNotificationCoordinator(focus,()=>true,sink)) {
                focus.Start(ShortSession());AdvanceFocus(focus);
                Check(focus.Phase==FocusPhase.Resting && store.Data.experience==1 && notifications.Attempts==1 && notifications.Accepted==0 && notifications.LastError!=null,"Notification failure affected settlement or was hidden");
                sink.fail=false;sink.reject=true;focus.Advance(60);
                Check(focus.Phase==FocusPhase.Finished && notifications.Accepted==0 && sink.calls.Count==2,"Native rejection changed Rest or falsely claimed acceptance");
            }
            var reloaded=new FocusController(Service(store));var reloadedSink=new NotificationSink();
            using(var notifications=new FocusNotificationCoordinator(reloaded,()=>true,reloadedSink)) {
                reloaded.Advance(60);reloaded.Configure(ShortSession());
                Check(reloadedSink.calls.Count==0,"Reload replayed historical notifications");
            }
        }
        {
            var store=new Store();var service=Service(store);
            for(int i=0;i<6;i++){service.BeginFocus(new FocusSessionOptions{focusMinutes=60},out var id);service.FinishFocus(id,3600,false);}
            var focus=new FocusController(service);var sink=new NotificationSink();
            using(var notifications=new FocusNotificationCoordinator(focus,()=>true,sink)) {
                focus.Start(ShortSession());AdvanceFocus(focus);
                Check(focus.LastResult.experience==0 && sink.calls.SequenceEqual(new[]{FocusNotificationKind.FocusCompleted}),"Reward cap suppressed a valid completion reminder");
            }
        }
    }

    static void VerifyTimerModes()
    {
        Check(FocusController.FormatTime(3599.1, true)=="59:59" && FocusController.FormatTime(3599.1, false)=="1:00:00"
            && FocusController.FormatTime(10800, true)=="3:00:00", "Timer formatting/rounding incorrect");
        foreach (var minutes in new[] { 1, 180 })
        {
            var store=new Store(); var service=Service(store); var focus=new FocusController(service);
            Check(focus.Start(new FocusSessionOptions { mode=FocusTimerMode.Countdown, focusMinutes=minutes, restMinutes=0 }), "Countdown rejected");
            AdvanceFocus(focus, minutes*60-1); Check(focus.Phase==FocusPhase.Focusing && focus.RemainingSeconds==1, "Countdown ended early");
            focus.Advance(1);
            Check(focus.Phase==FocusPhase.Finished && focus.RestSeconds==0 && focus.LastResult.experience==minutes
                && focus.LastResult.restDurationSeconds==0, "Countdown did not finish without Rest");
            var reloaded=JsonUtility.FromJson<PlayerData>(JsonUtility.ToJson(store.Data)); PlayerDataStore.Normalize(reloaded);
            Check(reloaded.focusSessions[0].mode==FocusTimerMode.Countdown && reloaded.focusSessions[0].restDurationSeconds==0, "Reload added Rest");
        }
        {
            var store=new Store(); var service=Service(store); var focus=new FocusController(service);
            Check(!focus.Configure(new FocusSessionOptions { mode=FocusTimerMode.Countdown, focusMinutes=181 })
                && !focus.Start(new FocusSessionOptions { mode=FocusTimerMode.Countdown, focusMinutes=0 })
                && !focus.Start(new FocusSessionOptions { mode=(FocusTimerMode)99 }), "Invalid mode/duration accepted");
            Check(focus.Start(new FocusSessionOptions { mode=FocusTimerMode.Stopwatch, restMinutes=0 }), "Stopwatch rejected");
            AdvanceFocus(focus, 4*3600);
            Check(focus.Phase==FocusPhase.Focusing && focus.ElapsedSeconds==14400 && focus.SpectatorFishCount==72, "Stopwatch stopped or preview exceeded session limit");
            Check(!focus.Configure(new FocusSessionOptions()) && !focus.Start(), "Running mode replaced");
            store.fail=true; Check(!focus.EndEarly() && focus.Phase==FocusPhase.Saving && store.Data.experience==0, "Failed stopwatch save paid or finished");
            focus.Advance(5); Check(focus.ElapsedSeconds==14400, "Saving accumulated time");
            store.fail=false; Check(focus.RetrySettlement(), "Stopwatch retry failed");
            Check(focus.Phase==FocusPhase.Finished && focus.LastResult.status==FocusSessionStatus.Completed
                && focus.LastResult.elapsedSeconds==14400 && focus.LastResult.eligibleSeconds==10800
                && focus.LastResult.experience==180 && focus.LastResult.fishCoins==72 && focus.LastResult.rawTickets==7 && focus.LastResult.tickets==4, "Stopwatch settlement wrong");
            Check(service.FinishFocus(focus.SessionId,14400,false) && !focus.EndEarly() && !focus.RetrySettlement()
                && store.Data.experience==180, "Repeated stopwatch settlement paid twice");
            Check(focus.Start(new FocusSessionOptions { mode=FocusTimerMode.Countdown, focusMinutes=180 }), "Second three-hour session rejected");
            AdvanceFocus(focus,10800);
            Check(store.Data.experience==360 && store.Data.fishCoins==144 && store.Data.tickets==4, "Mixed modes did not share daily limits");
            Check(focus.Start(new FocusSessionOptions()), "Capped session blocked"); AdvanceFocus(focus,1500);
            Check(focus.LastResult.experience==0 && focus.LastResult.fishCoins==0 && focus.LastResult.tickets==0 && focus.Phase==FocusPhase.Resting, "Capped session behavior wrong");
            Check(service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes)==25, "New modes lost daily goal");
            var data=JsonUtility.FromJson<PlayerData>(JsonUtility.ToJson(store.Data)); PlayerDataStore.Normalize(data);
            Check(data.rewardDays.Single().focusMinutes==360 && data.focusSessions[0].elapsedSeconds==14400
                && data.focusSessions[0].restDurationSeconds==0 && data.focusSessions[0].focusDurationSeconds==0, "Stopwatch reload corrupted receipt/allowance");
        }
        foreach (var mode in new[] { FocusTimerMode.Stopwatch, FocusTimerMode.Countdown })
        {
            var store=new Store(); var service=Service(store); var focus=new FocusController(service);
            focus.Start(new FocusSessionOptions { mode=mode }); AdvanceFocus(focus,359); focus.EndEarly();
            Check(focus.LastResult.experience==5 && focus.LastResult.fishCoins==2 && focus.LastResult.tickets==0
                && focus.LastResult.status==(mode==FocusTimerMode.Stopwatch?FocusSessionStatus.Completed:FocusSessionStatus.EndedEarly)
                && focus.Phase==FocusPhase.Finished, "Manual end rounding/status wrong");
            focus.Start(); AdvanceFocus(focus,60); focus.Cancel();
            Check(store.Data.experience==5 && focus.LastResult.status==FocusSessionStatus.Cancelled, "Cancel paid new mode");
            focus.Start(); focus.Advance(11);
            Check(focus.Phase==FocusPhase.Interrupted && focus.LastResult.status==FocusSessionStatus.Invalid && store.Data.experience==5, "Gap paid new mode");
            focus.Start(); var restarted=Service(store);
            Check(restarted.Snapshot.focusSessions.Last().status==FocusSessionStatus.Invalid, "Restart retained unverifiable new mode");
        }
        var legacy=JsonUtility.FromJson<PlayerData>("{\"experience\":120,\"rewardDays\":[{\"day\":\"2026-10-09\",\"focusMinutes\":120}],\"focusSessions\":[{\"id\":\"old\",\"eligibleMinutes\":25}]}");
        PlayerDataStore.Normalize(legacy);
        Check(legacy.experience==120 && legacy.rewardDays[0].focusMinutes==120 && legacy.focusSessions[0].mode==FocusTimerMode.Pomodoro
            && legacy.focusSessions[0].elapsedSeconds==1500, "Legacy allowance/reset or mode migration wrong");
    }
}
