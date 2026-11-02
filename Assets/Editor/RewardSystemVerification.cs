using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RewardSystemVerification
{
    private sealed class Clock : ITaskClock
    {
        public DateTime now = new DateTime(2026, 10, 8, 12, 0, 0);
        public DateTime LocalNow => now;
        public DateTime UtcNow => now.AddHours(-8);
    }
    private sealed class Store : IPlayerDataStore
    {
        public PlayerData Data { get; private set; } = new PlayerData();
        public Store(int experience=0) { Data.experience=experience; }
        public bool fail;
        public bool TrySave(PlayerData data, out string error)
        { error = fail ? "Simulated write failure." : null; if (fail) return false; Data = data; return true; }
    }
    static RewardSystemVerification() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists("Temp/verify-rewards.request")) return;
        File.Delete("Temp/verify-rewards.request");
        try { Run(); } catch (Exception ex) { Debug.LogException(ex); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Complete(TaskService service, TaskDifficulty difficulty = TaskDifficulty.Normal, bool daily = false)
    {
        var task = new QuestTask { title = Guid.NewGuid().ToString(), difficulty = difficulty, daily = daily,
            scheduled = daily, date = TaskService.DateKey(service.LocalNow) };
        Check(service.Upsert(task, null, out var id) && service.Complete(id, true), service.LastError);
        return id;
    }
    private static string Session(TaskService service, double seconds = 1500, bool early = false)
    {
        Check(service.BeginFocus(out string id) && service.FinishFocus(id, seconds, early), service.LastError);
        return id;
    }
    private static void VerifyRewardPreviewSnapshot()
    {
        var clock = new Clock();
        var store = new Store();
        var service = new TaskService(store, clock);
        var legacy = new RewardDay { day = TaskService.DateKey(clock.LocalNow), taskExperience = 4, focusMinutes = 239 };
        store.Data.rewardDays.Add(legacy);
        var preview = service.TodayRewards;
        Check(preview.firstTaskSettled && !legacy.rewardRulesInitialized, "Reading legacy rewards must not migrate the live save.");
        preview.focusMinutes = 0;
        Check(service.TodayRewards.focusMinutes == 239, "The caller mutated the cached allowance.");
        Check(service.PreviewFocusRewards(300).fishCoins == 0, "Focus preview ignored the daily allowance.");
        var next = service.Snapshot;
        next.rewardDays[0].focusMinutes = 0;
        store.fail = true;
        Check(!store.TrySave(next, out _) && service.TodayRewards.focusMinutes == 239, "Failed persistence invalidated the allowance.");
        store.fail = false;
        Check(store.TrySave(next, out _) && service.PreviewFocusRewards(300).fishCoins == 1, "Committed allowance did not refresh.");
        clock.now = clock.now.AddDays(1);
        Check(service.TodayRewards.taskExperience == 0 && store.Data.rewardDays.Count == 1, "Browsing a new day must not write a reward record.");
    }

    [MenuItem("Tools/Cat Quest/Verify Rewards and Focus")]
    public static void Run()
        => Run(CharacterHomeVerification.Run);

    internal static void Run(Action verifyCharacterHome)
    {
        VerifyRewardPreviewSnapshot();
        try
        {
            VerifyTasksAndGoals(); VerifyLevelTasks(); VerifyFocus(); VerifyController(); VerifyMigration();
            TaskSystemVerification.Run(); verifyCharacterHome();
            File.WriteAllText("Temp/reward-system.verification", "ALL PASS: difficulty tiers, independent caps, goals, undo, midnight, Focus settlement/retry/cancel/rest, interruption, migration, levels; existing task and CharacterHome checks passed.");
            Debug.Log("Rewards and Focus verification: ALL PASS");
        }
        catch (Exception ex) { File.WriteAllText("Temp/reward-system.verification", "FAIL: " + ex); throw; }
    }
    private static void VerifyTasksAndGoals()
    {
        var store = new Store(); var clock = new Clock(); var service = new TaskService(store, clock);
        Complete(service, TaskDifficulty.Easy); Complete(service); string hard = Complete(service, TaskDifficulty.Hard, true);
        Check(service.Snapshot.experience == 80 && service.Snapshot.fishCoins == 32, "Difficulty tiers are wrong.");
        Check(TaskService.DifficultyName(TaskDifficulty.Normal) == "Medium", "Middle tier label is wrong.");
        Check(service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 3 && service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 0, "Goals were combined.");
        var receipt = service.Query(TaskTab.History).Single(h => h.task.id == hard).history;
        Check(service.UndoCompletion(receipt.id) && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 2, "Undo did not decrease unclaimed progress.");
        Check(!service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests), "Incomplete goal can be claimed.");
        Check(service.Complete(hard, true) && service.Complete(hard, true) && service.Snapshot.experience == 80, "Recompletion paid twice.");
        store.fail = true;
        Check(!service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests) && !service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests), "Failed claim changed state.");
        store.fail = false;
        Check(service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests) && service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests), service.LastError);
        Check(service.Snapshot.experience == 100 && service.Snapshot.fishCoins == 52 && service.TodayRewards.taskExperience == 80, "Claim duplicated or consumed task cap.");
        Check(service.UndoCompletion(receipt.id) && service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests), "Undo revoked a paid claim.");
        Complete(service, TaskDifficulty.Hard); Complete(service, TaskDifficulty.Hard); Complete(service, TaskDifficulty.Hard); Complete(service, TaskDifficulty.Hard);
        Check(service.Snapshot.experience == 185 && service.Snapshot.fishCoins == 86, "Task cap failed.");
        Check(service.Snapshot.history.Last().experience == 0 && service.Snapshot.history.Last().fishCoins == 0, "Capped completion was not recorded.");
        clock.now = clock.now.AddDays(1); service.RefreshDate();
        Check(!service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests) && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 0 && service.TodayRewards.taskExperience == 0, "Midnight did not reset goals/caps.");
        Check(service.Complete(hard, true) && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 1, "New daily occurrence did not count.");

        var partial = new Store(); var partialService = new TaskService(partial, new Clock());
        Complete(partialService, TaskDifficulty.Easy);
        for (int i = 0; i < 3; i++) Complete(partialService, TaskDifficulty.Hard);
        Complete(partialService, TaskDifficulty.Hard);
        Check(partial.Data.experience == 165 && partial.Data.fishCoins == 76 && partial.Data.history.Last().experience == 30 && partial.Data.history.Last().fishCoins == 12, "Partial reward exceeded remaining task allowance.");
        // Currency allowances are independent, including migrated data with different XP/coin ratios.
        partial.Data.rewardDays[0].taskExperience = 165; partial.Data.rewardDays[0].taskFishCoins = 61;
        Complete(partialService, TaskDifficulty.Hard);
        Check(partial.Data.history.Last().experience == 0 && partial.Data.history.Last().fishCoins == 5, "Independent allowance clamping failed.");
    }
    private static void VerifyLevelTasks()
    {
        int[] experience = { 0, 1800, 13050 };
        int[,] xp = { {15,25,40}, {28,47,76}, {58,97,156} };
        int[,] fish = { {6,10,16}, {11,19,30}, {23,39,62} };
        int[] caps = {150,285,585}, fishCaps = {60,114,234};
        for(int level=0; level<3; level++) for(int difficulty=0; difficulty<3; difficulty++) {
            var store=new Store(); store.Data.experience=experience[level];
            var service=new TaskService(store,new Clock());
            Check(service.TaskExperienceLimit==caps[level] && service.TaskFishLimit==fishCaps[level],"Scaled task caps");
            Check(service.Upsert(new QuestTask {title="Level reward",difficulty=(TaskDifficulty)difficulty},null,out var id),service.LastError);
            var preview=service.PreviewReward(service.Query(TaskTab.Todo).Single());
            Check(preview.experience==xp[level,difficulty] && preview.fishCoins==fish[level,difficulty],"Task level preview");
            store.fail=true; Check(!service.Complete(id,true) && store.Data.history.Count==0 && store.Data.experience==experience[level],"Failed task write paid");
            store.fail=false; Check(service.Complete(id,true) && service.Complete(id,true),service.LastError);
            Check(store.Data.experience==experience[level]+xp[level,difficulty] && store.Data.fishCoins==fish[level,difficulty]
                && store.Data.tickets==1 && store.Data.history.Count==1,"Task retry/duplicate/scaling");
        }
        {
            var store=new Store(); var clock=new Clock(); store.Data.experience=13050;
            // Recover actual paid amounts from history when the day's aggregate is absent.
            store.Data.history.Add(new TaskCompletion { id="paid",taskId="old",day="once",rewardedLocalDay="2026-10-08",
                completedLocalDay="2026-10-08",snapshot=new QuestTask(),experience=460,fishCoins=230 });
            var service=new TaskService(store,clock);
            Check(service.TodayRewards.taskExperience==460 && service.TodayRewards.taskFishCoins==230,"Rebuilt allowance truncated high-level receipts");
            string id=Complete(service,TaskDifficulty.Hard);
            var receipt=store.Data.history.Last();
            Check(receipt.experience==125 && receipt.fishCoins==4 && service.TodayRewards.taskExperience==585
                && service.TodayRewards.taskFishCoins==234,"High-level partial cap");
            Complete(service); Check(store.Data.history.Last().experience==0 && store.Data.history.Last().fishCoins==0,"Capped task not recorded");
            int paid=store.Data.experience;
            Check(service.UndoCompletion(receipt.id) && service.Complete(id,true) && store.Data.experience==paid
                && service.TodayRewards.taskExperience==585,"Undo released allowance or repaid");
            string folder="Library/TaskLevelVerification/"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(folder);
            var disk=new PlayerDataStore(folder+"/save.json"); Check(disk.TrySave(store.Data,out var error),error);
            var reloaded=new TaskService(new PlayerDataStore(folder+"/save.json"),clock);
            Check(reloaded.Complete(id,true) && reloaded.Snapshot.experience==paid && reloaded.TodayRewards.taskExperience==585,"Reload repaid task");
            clock.now=clock.now.AddDays(1); reloaded.RefreshDate();
            Check(reloaded.TodayRewards.taskExperience==0 && reloaded.TodayRewards.taskFishCoins==0,"Task cap did not reset at midnight");
            Complete(reloaded); Check(reloaded.Snapshot.history.Last().experience==97 && reloaded.Snapshot.history.Last().fishCoins==39,"New day lost scaled rewards");
        }
    }

    private static void VerifyFocus()
    {
        var cumulativeClock = new Clock();
        var cumulativeStore = new Store(100);
        var cumulative = new TaskService(cumulativeStore, cumulativeClock);
        Session(cumulative, 750, true);
        Check(cumulative.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 12 && !cumulative.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), "Partial Focus goal progress incorrect.");
        Session(cumulative, 750, true);
        Check(cumulative.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 25 && cumulative.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), "Two early sessions did not accumulate seconds toward the daily goal.");
        var reloadedCumulative = new TaskService(cumulativeStore, cumulativeClock);
        Check(reloadedCumulative.DailyGoalClaimed(DailyRewardGoal.Focus25Minutes), "Cumulative Focus claim lost on reload.");
        cumulativeClock.now = cumulativeClock.now.AddDays(1); cumulative.RefreshDate();
        Check(cumulative.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 0, "Cumulative Focus did not reset next day.");
        Session(cumulative, 600, true);
        Check(cumulative.BeginFocus(out string ignored) && cumulative.CancelFocus(ignored), cumulative.LastError);
        Check(cumulative.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 10, "Cancelled session counted toward Focus goal.");
        var clock = new Clock(); var store = new Store(100); var service = new TaskService(store, clock);
        Check(service.BeginFocus(out string cancelled) && service.CancelFocus(cancelled), service.LastError);
        Check(!service.FinishFocus(cancelled, 1500, false) && service.Snapshot.experience == 100, "Cancelled Focus rewarded.");
        Check(!service.FinishFocus("missing", 1500, false), "Missing Focus rewarded.");
        string shortId = Session(service, 1499, true);
        var shortResult = service.Snapshot.focusSessions.Single(s => s.id == shortId);
        Check(shortResult.eligibleMinutes == 24 && shortResult.experience == 13 && shortResult.fishCoins == 4 && shortResult.tickets == 0, "Early result did not use complete minutes.");
        Check(service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 24 && !service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), "Short Focus unlocked daily goal.");
        string id = Session(service);
        Check(service.FinishFocus(id, 1500, false) && service.Snapshot.experience == 126 && service.Snapshot.fishCoins == 9 && service.Snapshot.tickets == 1, "Focus reward tiers or idempotency failed.");
        Check(service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes) && service.Snapshot.experience == 137 && !service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests), "Independent Focus claim failed.");
        clock.now = clock.now.AddDays(1); service.RefreshDate();
        for (int i = 0; i < 16; i++) Session(service);
        Check(service.TodayRewards.focusMinutes == 240 && service.TodayRewards.focusTickets == 3 && service.Snapshot.tickets == 4, "Focus daily caps failed.");
        var sessions = service.Snapshot.focusSessions.Where(s => s.rewardDay == TaskService.DateKey(clock.now)).ToArray();
        Check(sessions[9].experience == 9 && sessions[9].fishCoins == 3 && sessions[9].tickets == 0 && sessions[10].experience == 0, "Focus level scaling failed.");
        // Explicit level-30 boundary fixture; previous sessions already checked natural level increases.
        store.Data.experience = 13050;
        store.Data.rewardDays.Single(d => d.day == TaskService.DateKey(clock.now)).focusMinutes = 230;
        string partialId = Session(service); string cappedId = Session(service);
        var partial = service.Snapshot.focusSessions.Single(s => s.id == partialId);
        Check(partial.rewardedMinutes == 10 && partial.experience == 19 && partial.fishCoins == 7
            && service.Snapshot.focusSessions.Single(s => s.id == cappedId).experience == 0
            && service.TodayRewards.focusMinutes == 240, "Focus partial allowance failed.");
        Check(service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 25 && service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), "Capped Focus did not satisfy objective.");
        clock.now = new DateTime(2026, 10, 10, 23, 59, 0); service.RefreshDate();
        Check(service.BeginFocus(out string across), service.LastError);
        clock.now = clock.now.AddMinutes(25); service.RefreshDate();
        Check(service.FinishFocus(across, 1500, false) && service.TodayRewards.focusMinutes == 25, "Cross-midnight session did not use settlement day.");
        Check(service.BeginFocus(out string pending), service.LastError);
        var restarted = new TaskService(store, clock);
        Check(restarted.Snapshot.focusSessions.Single(s => s.id == pending).status == FocusSessionStatus.Invalid && !restarted.FinishFocus(pending, 1500, false), "Restart rewarded an unverifiable session.");
    }
    private static void VerifyController()
    {
        var store = new Store(100); var service = new TaskService(store, new Clock()); var controller = new FocusController(service);
        Check(controller.Start(), service.LastError);
        for (int i = 0; i < 1500; i++) controller.Advance(1);
        Check(controller.Phase == FocusPhase.Resting && controller.LastResult.experience == 13, "Normal completion did not save before Rest.");
        int paid = service.Snapshot.experience;
        controller.Advance(300);
        Check(controller.Phase == FocusPhase.Finished && service.Snapshot.experience == paid, "Rest grants rewards or never ends.");
        Check(controller.Start(), service.LastError);
        for (int i = 0; i < 61; i++) controller.Advance(1);
        store.fail = true;
        Check(!controller.EndEarly() && controller.Phase == FocusPhase.Saving && service.Snapshot.experience == paid, "Failed processing started Rest or paid reward.");
        controller.Advance(100);
        Check(controller.ElapsedSeconds == 61 && !controller.Start(), "Pending result continued timing or allowed another session.");
        store.fail = false;
        Check(controller.RetrySettlement() && controller.Phase == FocusPhase.Resting && service.Snapshot.experience == paid, "Retry failed.");
        controller.EndRest(); Check(controller.Phase == FocusPhase.Finished && service.Snapshot.experience == paid, "Early Rest revoked rewards.");
        Check(controller.Start() && controller.Cancel() && controller.Phase == FocusPhase.Interrupted, "Cancel did not bypass Rest.");
        Check(controller.Start(), service.LastError); controller.Advance(11);
        Check(controller.Phase == FocusPhase.Interrupted && controller.LastResult.status == FocusSessionStatus.Invalid && controller.LastResult.experience == 0, "Suspension gap paid rewards.");
    }
    private static void VerifyMigration()
    {
        var folder = "Library/RewardSystemVerification/" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(folder);
        string path = folder + "/save.json";
        var legacy = new PlayerData { version = 2, username = "Legacy", experience = 110, fishCoins = 55, tickets = 7 };
        legacy.history.Add(new TaskCompletion { id = "old", taskId = "old-task", day = "once", completedUtc = "2026-10-08T04:00:00Z",
            completedLocalDay = "2026-10-08", rewardedLocalDay = "2026-10-08", snapshot = new QuestTask(), experience = 110, fishCoins = 55 });
        File.WriteAllText(path, JsonUtility.ToJson(legacy));
        var disk = new PlayerDataStore(path); var service = new TaskService(disk, new Clock());
        Check(disk.Data.version == 5 && disk.Data.username == "Legacy" && disk.Data.tickets == 7 && disk.Data.experience == 110, "Migration changed previous rewards.");
        Complete(service, TaskDifficulty.Hard);
        Check(service.Snapshot.experience == 154 && service.Snapshot.fishCoins == 66, "Legacy paid rewards did not consume allowance.");
        Session(service); Check(service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), service.LastError);
        var reloaded = new TaskService(new PlayerDataStore(path), new Clock());
        Check(reloaded.DailyGoalClaimed(DailyRewardGoal.Focus25Minutes) && reloaded.Snapshot.tickets == 8, "Claim or Focus receipt lost on reload.");
        TaskService.CatLevel(225, out int level, out int xp, out int required);
        Check(level == 3 && xp == 0 && required == 150, "Multiple level-ups failed.");
        TaskService.CatLevel(99, out level, out xp, out required);
        Check(level == 1 && xp == 99 && required == 100, "Level boundary failed.");
    }
}
