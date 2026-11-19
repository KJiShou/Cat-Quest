using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Opt-in, isolated domain checks followed by the existing aggregate exactly once.
[InitializeOnLoad]
public static class RewardBalanceVerification
{
    sealed class Store : IPlayerDataStore {
        public PlayerData Data { get; private set; } = new PlayerData();
        public bool fail;
        public bool TrySave(PlayerData value, out string error) { error=fail?"Injected write failure":null; if(fail)return false; Data=value; return true; }
    }
    sealed class Clock : ITaskClock {
        public DateTime now=new DateTime(2026,10,31,12,0,0);
        public DateTime LocalNow=>now; public DateTime UtcNow=>now.AddHours(-8);
    }
    static RewardBalanceVerification() { EditorApplication.update+=Poll; }
    static void Poll() {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-reward-balance.request"))return;
        File.Delete("Temp/verify-reward-balance.request");
        string started=DateTime.UtcNow.ToString("o");
        try { Run(); File.WriteAllText("Temp/verify-reward-balance.result","PASS "+started+" -> "+DateTime.UtcNow.ToString("o")+" | Unity "+Application.unityVersion+"\nFirst-task ticket, two goal tiers, caps/rounding, atomic failure/retry, undo/cross-day/reload, legacy bytes/backup/paid receipts, level bonuses and LevelProgression -> Equipment -> Battle -> Focus -> Rewards -> Tasks/CharacterHome."); }
        catch(Exception e) { File.WriteAllText("Temp/verify-reward-balance.result","FAIL "+started+"\n"+e); Debug.LogException(e); }
    }
    static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
    static string Create(TaskService s,string title="Work",bool daily=false) {
        Check(s.Upsert(new QuestTask{title=title,daily=daily,scheduled=daily,date=TaskService.DateKey(s.LocalNow)},null,out var id),s.LastError); return id;
    }
    public static void Run() {
        UpgradeFirstTicket(); Goals(); Legacy(); Boundaries(); MultiLevel();
        LevelProgressionVerification.Run();
    }
    static void UpgradeFirstTicket() {
        var store=new Store();var clock=new Clock();var service=new TaskService(store,clock);
        Check(service.Complete(Create(service, "Daily", true),true),service.LastError);
        var oldData=store.Data;
        oldData.rewardDays.Single().taskTickets=1;
        oldData.history.Single().tickets=1;
        oldData.tickets=1;
        service=new TaskService(store,clock);
        Check(service.PreviewDailyGoal(DailyRewardGoal.FirstQuest).tickets==0
            && service.ClaimDailyGoal(DailyRewardGoal.FirstQuest) && store.Data.tickets==1,
            "Upgrade repaid the old automatic Ticket on claim");
        clock.now=clock.now.AddDays(1);service.RefreshDate();
        Check(service.Complete(oldData.tasks.Single().id,true) && store.Data.tickets==1
            && service.ClaimDailyGoal(DailyRewardGoal.FirstQuest) && store.Data.tickets==2,
            "Next day did not grant exactly one mission Ticket");
    }
    static void Goals() {
        var store=new Store();var s=new TaskService(store,new Clock());
        Check(s.CurrentTaskGoal==DailyRewardGoal.FirstQuest&&!s.ClaimDailyGoal(DailyRewardGoal.FirstQuest),"First goal available without work");
        Check(s.Complete(Create(s),true),s.LastError);store.fail=true;
        Check(!s.ClaimDailyGoal(DailyRewardGoal.FirstQuest)&&!s.DailyGoalClaimed(DailyRewardGoal.FirstQuest)&&s.Snapshot.experience==25&&s.Snapshot.tickets==0,"Failed first goal paid");
        store.fail=false;Check(s.ClaimDailyGoal(DailyRewardGoal.FirstQuest)&&s.ClaimDailyGoal(DailyRewardGoal.FirstQuest),s.LastError);
        Check(s.Snapshot.experience==35&&s.Snapshot.fishCoins==15&&s.Snapshot.tickets==1&&s.EquipmentUnlocked&&s.CurrentTaskGoal==DailyRewardGoal.ThreeQuests,"First goal payout/access/next tier");
        Check(s.Complete(Create(s),true)&&s.Complete(Create(s),true)&&s.ClaimDailyGoal(DailyRewardGoal.ThreeQuests)&&s.ClaimDailyGoal(DailyRewardGoal.ThreeQuests),s.LastError);
        Check(s.Snapshot.experience==105&&s.Snapshot.fishCoins==55&&s.Snapshot.tickets==4&&s.TodayRewards.questsTickets==3&&s.TodayRewards.taskExperience==75,"Both mission tiers pay once outside task caps");
        Check(s.PreviewDailyGoal(DailyRewardGoal.FirstQuest).experience==10&&s.PreviewDailyGoal(DailyRewardGoal.ThreeQuests).experience==20&&s.PreviewDailyGoal(DailyRewardGoal.ThreeQuests).tickets==3,"Claimed goals repriced after level up");
        Check(s.UndoCompletion(s.Snapshot.history[0].id)&&s.ClaimDailyGoal(DailyRewardGoal.FirstQuest)&&s.Snapshot.experience==105,"Undo repaid first goal");
    }
    static void Legacy() {
        string folder="Library/RewardBalanceVerification/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);string path=folder+"/legacy.json";
        string original="{\"version\":5,\"username\":\"Keep\",\"tagsInitialized\":true,\"experience\":1800,\"fishCoins\":200,\"tickets\":7,\"rewardDays\":[{\"day\":\"2026-10-31\",\"taskExperience\":20,\"taskFishCoins\":10,\"focusMinutes\":600,\"focusTickets\":12,\"focusClaimed\":true}],\"history\":[{\"id\":\"old\",\"taskId\":\"old\",\"rewardedLocalDay\":\"2026-10-31\",\"completedLocalDay\":\"2026-10-31\",\"day\":\"once\",\"experience\":20,\"fishCoins\":10,\"snapshot\":{\"title\":\"Old task\"}}]}";
        File.WriteAllText(path,original);var disk=new PlayerDataStore(path);var clock=new Clock();var s=new TaskService(disk,clock);
        Check(s.CurrentTaskGoal==DailyRewardGoal.ThreeQuests&&s.TodayRewards.firstTaskSettled&&s.TodayRewards.taskTickets==0,"Legacy day offered retroactive first reward");
        Check(s.PreviewDailyGoal(DailyRewardGoal.Focus25Minutes).experience==20&&s.PreviewDailyGoal(DailyRewardGoal.Focus25Minutes).fishCoins==10,"Legacy claimed goal fallback repriced");
        Check(File.ReadAllText(path)==original&&s.Snapshot.tickets==7&&s.Snapshot.fishCoins==200,"Reading legacy save mutated it");
        string id=Create(s);Check(File.ReadAllText(path+".bak")==original,"First normal write lost byte-exact backup");
        Check(s.Complete(id,true)&&s.Snapshot.tickets==7,"Second task on old day backfilled ticket");
        Check(s.BeginFocus(out var focus)&&s.FinishFocus(focus,1500,false)&&s.Snapshot.tickets==7&&s.TodayRewards.focusMinutes==600&&s.TodayRewards.focusTickets==12,"Old over-cap day was reset or clawed back");
        Check(s.Snapshot.history.First().experience==20&&s.Snapshot.history.First().fishCoins==10&&s.Snapshot.history.First().tickets==0,"Old task receipt repriced");
        var reload=new TaskService(new PlayerDataStore(path),clock);Check(reload.TodayRewards.firstTaskSettled&&reload.TodayRewards.taskTickets==0,"Legacy blocked first reward lost on reload");
        clock.now=clock.now.AddDays(1);reload.RefreshDate();Check(reload.Complete(Create(reload),true)&&reload.ClaimDailyGoal(DailyRewardGoal.FirstQuest)&&reload.TodayRewards.taskTickets==1,"Legacy save did not adopt next-day rules");
    }
    static void Boundaries() {
        var store=new Store();var clock=new Clock();var s=new TaskService(store,clock);
        store.Data.rewardDays.Add(new RewardDay{day="2026-10-31",rewardRulesInitialized=true,taskExperience=150,taskFishCoins=60});
        Check(s.Complete(Create(s),true)&&s.Snapshot.history.Single().experience==0&&s.Snapshot.history.Single().fishCoins==0&&s.Snapshot.tickets==0&&s.ClaimDailyGoal(DailyRewardGoal.FirstQuest)&&s.Snapshot.tickets==1,"Task cap suppressed first mission Ticket");
        var p=TaskService.FocusRewardsForSeconds(1500,0,0,2);Check(p.experience==13&&p.fishCoins==5&&p.tickets==1,"Focus final rounding");
        p=TaskService.FocusRewardsForSeconds(18000,0,1,30);Check(p.experience==468&&p.fishCoins==187&&p.tickets==9,"Session/ticket caps");
        p=TaskService.FocusRewardsForSeconds(1500,479,10,30);Check(p.experience==1&&p.fishCoins==0&&p.tickets==0,"One-minute remaining allowance rounding");
        p=TaskService.FocusRewardsForSeconds(1500,600,12,30);Check(p.experience==0&&p.fishCoins==0&&p.tickets==0,"Legacy used allowance became negative reward");
        foreach(double seconds in new[]{-1d,double.NaN,double.PositiveInfinity})Check(TaskService.FocusRewardsForSeconds(seconds,0,0,30).experience==0,"Invalid elapsed time rewarded");
        store=new Store();store.Data.experience=549;s=new TaskService(store,clock);
        Check(s.Complete(Create(s),true)&&s.Snapshot.tickets==1&&s.ClaimDailyGoal(DailyRewardGoal.FirstQuest)&&s.Snapshot.tickets==2&&s.TodayRewards.taskTickets==1,"Level milestone consumed first-task allowance");
        Check(s.BeginFocus(new FocusSessionOptions{mode=FocusTimerMode.Stopwatch},out var id)&&s.FinishFocus(id,4500,true)&&s.TodayRewards.focusTickets==3&&s.Snapshot.tickets==5,"Task/Focus/level Ticket budgets mixed");
    }
    static void MultiLevel() {
        // Directly exercise the shared atomic commit boundary: lower Focus rewards no longer
        // span multiple levels in one session, but Boss payouts still use this same boundary.
        var store=new Store();store.Data.experience=99;var s=new TaskService(store,new Clock());int events=0;
        s.LevelUp+=r=>{events++;Check(r.fromLevel==1&&r.toLevel==4&&r.fishCoins==45&&store.Data.experience==399,"Multi-level bonus/event before save");};
        var commit=typeof(TaskService).GetMethod("Commit",BindingFlags.Instance|BindingFlags.NonPublic);
        Action<PlayerData> award=d=>d.experience+=300;store.fail=true;
        Check(!(bool)commit.Invoke(s,new object[]{award})&&store.Data.experience==99&&events==0,"Multi-level failed save mutated wallet");
        store.fail=false;Check((bool)commit.Invoke(s,new object[]{award})&&store.Data.fishCoins==45&&events==1,"Multi-level sum");
    }
}
