using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LevelProgressionVerification
{
    sealed class Store:IPlayerDataStore {
        public PlayerData Data {get;private set;}=new PlayerData(); public bool fail;
        public bool TrySave(PlayerData value,out string error) {error=fail?"Test write failure":null;if(fail)return false;Data=value;return true;}
    }
    sealed class Clock:ITaskClock { public DateTime LocalNow=>new DateTime(2026,10,25,12,0,0); public DateTime UtcNow=>LocalNow.AddHours(-8); }
    static LevelProgressionVerification(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-level-progression.request"))return;
        File.Delete("Temp/verify-level-progression.request");
        try{Run();File.WriteAllText("Temp/verify-level-progression.result","PASS "+DateTime.UtcNow.ToString("o")+" | Unity "+Application.unityVersion+"\nDaily goal scaling/receipt/legacy, atomic level rewards, multi-level, milestone Tickets outside Focus caps, retry/duplicate/undo/reload and full Equipment->Battle->Focus->Reward chain.");}
        catch(Exception e){File.WriteAllText("Temp/verify-level-progression.result","FAIL "+DateTime.UtcNow.ToString("o")+"\n"+e);Debug.LogException(e);}
    }
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    public static void Run()
    {
        {
            var store=new Store();store.Data.experience=224;var service=new TaskService(store,new Clock());int events=0;
            TaskService.LevelUpReward observed=default;service.LevelUp+=r=>{events++;observed=r;Check(store.Data.experience==323,"Notification before persistence");};
            Check(service.BeginFocus(new FocusSessionOptions{mode=FocusTimerMode.Stopwatch},out var id),service.LastError);
            store.fail=true;Check(!service.FinishFocus(id,10800,true)&&store.Data.experience==224&&events==0,"Failed write awarded level");
            store.fail=false;Check(service.FinishFocus(id,10800,true),service.LastError);
            Check(events==1&&observed.fromLevel==2&&observed.toLevel==3&&observed.fishCoins==15&&observed.tickets==0,"Level transition bonus");
            Check(store.Data.fishCoins==54&&store.Data.tickets==3&&store.Data.focusSessions.Last().fishCoins==39,"Bonus polluted Focus receipt");
            Check(service.FinishFocus(id,10800,true)&&events==1&&store.Data.fishCoins==54,"Duplicate level payout");
            string folder="Library/LevelProgressionVerification/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);
            var disk=new PlayerDataStore(folder+"/save.json");Check(disk.TrySave(store.Data,out var error),error);
            var loaded=new PlayerDataStore(folder+"/save.json");var restarted=new TaskService(loaded,new Clock());
            Check(restarted.FinishFocus(id,10800,true)&&loaded.Data.fishCoins==54,"Reload backfilled bonus");
        }
        {
            var store=new Store();store.Data.experience=549;var service=new TaskService(store,new Clock());
            Check(service.BeginFocus(new FocusSessionOptions{mode=FocusTimerMode.Stopwatch},out var id)&&service.FinishFocus(id,10800,true),service.LastError);
            Check(store.Data.experience==666&&store.Data.fishCoins==71&&store.Data.tickets==4
                &&service.TodayRewards.focusTickets==3&&store.Data.focusSessions.Last().tickets==3,"Level-five Ticket consumed Focus cap");
        }
        {
            var store=new Store();store.Data.experience=90;var service=new TaskService(store,new Clock());
            Check(service.Upsert(new QuestTask{title="Level boundary"},null,out var id)&&service.Complete(id,true),service.LastError);
            Check(store.Data.experience==115&&store.Data.fishCoins==20&&service.TodayRewards.taskFishCoins==10,"Task bonus entered daily allowance");
            var receipt=store.Data.history.Single();Check(service.UndoCompletion(receipt.id)&&service.Complete(id,true)&&store.Data.fishCoins==20,"Undo repaid level");
        }
        foreach(var goal in new[]{DailyRewardGoal.ThreeQuests,DailyRewardGoal.Focus25Minutes}) {
            var store=new Store();store.Data.experience=13050;var service=new TaskService(store,new Clock());
            if(goal==DailyRewardGoal.ThreeQuests)for(int i=0;i<3;i++) {service.Upsert(new QuestTask{title="Goal "+i},null,out var id);service.Complete(id,true);}
            else {service.BeginFocus(out var id);service.FinishFocus(id,1500,false);}
            int expectedXp=goal==DailyRewardGoal.ThreeQuests?78:39, expectedFish=goal==DailyRewardGoal.ThreeQuests?39:19;
            var preview=service.PreviewDailyGoal(goal);Check(preview.experience==expectedXp&&preview.fishCoins==expectedFish,"Daily level multiplier");
            int xp=store.Data.experience,fish=store.Data.fishCoins;store.fail=true;
            Check(!service.ClaimDailyGoal(goal)&&!service.DailyGoalClaimed(goal)&&store.Data.fishCoins==fish,"Daily failure paid");
            store.fail=false;Check(service.ClaimDailyGoal(goal)&&service.ClaimDailyGoal(goal)&&store.Data.experience==xp+expectedXp&&store.Data.fishCoins==fish+expectedFish,"Daily duplicate");
            store.Data.experience=34300;Check(service.PreviewDailyGoal(goal).experience==expectedXp&&service.PreviewDailyGoal(goal).fishCoins==expectedFish,"Claimed card repriced");
        }
        {
            var store=new Store();store.Data.experience=13050;store.Data.rewardDays.Add(new RewardDay{day="2026-10-25",questsClaimed=true});
            var service=new TaskService(store,new Clock());Check(service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests)&&store.Data.fishCoins==0
                &&service.PreviewDailyGoal(DailyRewardGoal.ThreeQuests).experience==20,"Legacy claim backfilled");
        }
        EquipmentVerification.Run();
    }
}
