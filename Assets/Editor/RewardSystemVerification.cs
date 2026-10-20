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
    [MenuItem("Tools/Cat Quest/Verify Rewards and Focus")]
    public static void Run()
    {
        try
        {
            VerifyTasksAndGoals(); VerifyFocus(); VerifyController(); VerifyMigration();
            TaskSystemVerification.Run(); CharacterHomeVerification.Run();
            File.WriteAllText("Temp/reward-system.verification", "ALL PASS: difficulty tiers, independent caps, goals, undo, midnight, Focus settlement/retry/cancel/rest, interruption, migration, levels; existing task and CharacterHome checks passed.");
            Debug.Log("Rewards and Focus verification: ALL PASS");
        }
        catch (Exception ex) { File.WriteAllText("Temp/reward-system.verification", "FAIL: " + ex); throw; }
    }
    private static void VerifyTasksAndGoals()
    {
        var store = new Store(); var clock = new Clock(); var service = new TaskService(store, clock);
        Complete(service, TaskDifficulty.Easy); Complete(service); string hard = Complete(service, TaskDifficulty.Hard, true);
        Check(service.Snapshot.experience == 60 && service.Snapshot.fishCoins == 30, "Difficulty tiers are wrong.");
        Check(TaskService.DifficultyName(TaskDifficulty.Normal) == "Medium", "Middle tier label is wrong.");
        Check(service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 3 && service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 0, "Goals were combined.");
        var receipt = service.Query(TaskTab.History).Single(h => h.task.id == hard).history;
        Check(service.UndoCompletion(receipt.id) && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 2, "Undo did not decrease unclaimed progress.");
        Check(!service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests), "Incomplete goal can be claimed.");
        Check(service.Complete(hard, true) && service.Complete(hard, true) && service.Snapshot.experience == 60, "Recompletion paid twice.");
        store.fail = true;
        Check(!service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests) && !service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests), "Failed claim changed state.");
        store.fail = false;
        Check(service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests) && service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests), service.LastError);
        Check(service.Snapshot.experience == 80 && service.Snapshot.fishCoins == 40 && service.TodayRewards.taskExperience == 60, "Claim duplicated or consumed task cap.");
        Check(service.UndoCompletion(receipt.id) && service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests), "Undo revoked a paid claim.");
        Complete(service, TaskDifficulty.Hard); Complete(service, TaskDifficulty.Hard); Complete(service, TaskDifficulty.Hard);
        Check(service.Snapshot.experience == 140 && service.Snapshot.fishCoins == 70, "Task cap failed.");
        Check(service.Snapshot.history.Last().experience == 0 && service.Snapshot.history.Last().fishCoins == 0, "Capped completion was not recorded.");
        clock.now = clock.now.AddDays(1); service.RefreshDate();
        Check(!service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests) && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 0 && service.TodayRewards.taskExperience == 0, "Midnight did not reset goals/caps.");
        Check(service.Complete(hard, true) && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 1, "New daily occurrence did not count.");

        var partial = new Store(); var partialService = new TaskService(partial, new Clock());
        Complete(partialService, TaskDifficulty.Easy);
        for (int i = 0; i < 3; i++) Complete(partialService, TaskDifficulty.Hard);
        Complete(partialService, TaskDifficulty.Hard);
        Check(partial.Data.experience == 120 && partial.Data.fishCoins == 60 && partial.Data.history.Last().experience == 20 && partial.Data.history.Last().fishCoins == 10, "Partial reward exceeded remaining task allowance.");
        // Currency allowances are independent, including migrated data with different XP/coin ratios.
        partial.Data.rewardDays[0].taskExperience = 120; partial.Data.rewardDays[0].taskFishCoins = 55;
        Complete(partialService, TaskDifficulty.Hard);
        Check(partial.Data.history.Last().experience == 0 && partial.Data.history.Last().fishCoins == 5, "Independent allowance clamping failed.");
    }
    private static void VerifyFocus()
    {
        var cumulativeClock = new Clock();
        var cumulativeStore = new Store();
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
        var clock = new Clock(); var store = new Store(); var service = new TaskService(store, clock);
        Check(service.BeginFocus(out string cancelled) && service.CancelFocus(cancelled), service.LastError);
        Check(!service.FinishFocus(cancelled, 1500, false) && service.Snapshot.experience == 0, "Cancelled Focus rewarded.");
        Check(!service.FinishFocus("missing", 1500, false), "Missing Focus rewarded.");
        string shortId = Session(service, 1499, true);
        var shortResult = service.Snapshot.focusSessions.Single(s => s.id == shortId);
        Check(shortResult.eligibleMinutes == 24 && shortResult.experience == 24 && shortResult.fishCoins == 8 && shortResult.tickets == 0, "Early result did not use complete minutes.");
        Check(service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 24 && !service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), "Short Focus unlocked daily goal.");
        string id = Session(service);
        Check(service.FinishFocus(id, 1500, false) && service.Snapshot.experience == 49 && service.Snapshot.fishCoins == 18 && service.Snapshot.tickets == 1, "Focus reward tiers or idempotency failed.");
        Check(service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes) && service.Snapshot.experience == 69 && !service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests), "Independent Focus claim failed.");
        clock.now = clock.now.AddDays(1); service.RefreshDate();
        for (int i = 0; i < 6; i++) Session(service);
        Check(service.TodayRewards.focusMinutes == 120 && service.TodayRewards.focusTickets == 4 && service.Snapshot.tickets == 5, "Focus daily caps failed.");
        var sessions = service.Snapshot.focusSessions.Where(s => s.rewardDay == TaskService.DateKey(clock.now)).ToArray();
        Check(sessions[4].experience == 20 && sessions[4].fishCoins == 8 && sessions[4].tickets == 0 && sessions[5].experience == 0, "Focus partial allowance failed.");
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
        var store = new Store(); var service = new TaskService(store, new Clock()); var controller = new FocusController(service);
        Check(controller.Start(), service.LastError);
        for (int i = 0; i < 1500; i++) controller.Advance(1);
        Check(controller.Phase == FocusPhase.Resting && controller.LastResult.experience == 25, "Normal completion did not save before Rest.");
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
        Check(controller.RetrySettlement() && controller.Phase == FocusPhase.Resting && service.Snapshot.experience == paid + 1, "Retry failed.");
        controller.EndRest(); Check(controller.Phase == FocusPhase.Finished && service.Snapshot.experience == paid + 1, "Early Rest revoked rewards.");
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
        Check(service.Snapshot.experience == 120 && service.Snapshot.fishCoins == 60, "Legacy paid rewards did not consume allowance.");
        Session(service); Check(service.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes), service.LastError);
        var reloaded = new TaskService(new PlayerDataStore(path), new Clock());
        Check(reloaded.DailyGoalClaimed(DailyRewardGoal.Focus25Minutes) && reloaded.Snapshot.tickets == 8, "Claim or Focus receipt lost on reload.");
        TaskService.CatLevel(225, out int level, out int xp, out int required);
        Check(level == 3 && xp == 0 && required == 150, "Multiple level-ups failed.");
        TaskService.CatLevel(99, out level, out xp, out required);
        Check(level == 1 && xp == 99 && required == 100, "Level boundary failed.");
    }
}
