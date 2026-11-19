using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static class TutorialRulesVerification
{
    sealed class Clock : ITaskClock { public DateTime LocalNow => new DateTime(2026,10,30,12,0,0); public DateTime UtcNow => LocalNow.AddHours(-8); }
    sealed class Store : IPlayerDataStore
    {
        public PlayerData Data {get; private set;} = new PlayerData(); public bool fail;
        public bool TrySave(PlayerData candidate,out string error) { error=fail?"Injected failure":null; if(fail)return false; Data=candidate;return true; }
    }
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    public static void Run()
    {
        var store=new Store();var service=new TaskService(store,new Clock());
        Check(!service.CanAccess(ProgressionFeature.Focus)&&!service.BeginFocus(out _)&&!service.BeginBattle(1,0,out _),"Lv.1 bypass");
        Check(service.Snapshot.focusSessions.Count==0&&service.Snapshot.battles.Count==0,"Locked operation mutated state");
        Check(service.InitializeTutorial()&&service.Tutorial.taskStep==4,"Legacy save auto-started guide");
        var progress=new TutorialProgress{taskStep=2};Check(service.SaveTutorial(progress),service.LastError);
        store.fail=true;Check(!service.Upsert(new QuestTask{title="Real task",difficulty=TaskDifficulty.Normal},null,out _)&&service.Tutorial.taskStep==2,"Failed task advanced guide");
        store.fail=false;Check(service.Upsert(new QuestTask{title="Real task",difficulty=TaskDifficulty.Normal},null,out var id),service.LastError);
        Check(service.Tutorial.taskStep==3&&service.Tutorial.taskId==id&&service.Snapshot.history.Count==0,"Task receipt or false completion");
        Check(service.Complete(id)&&service.Complete(id),service.LastError);
        // Current Medium task: 25 EXP and 10 Fish at Lv.1. Mission tickets require a claim.
        var completed=service.Snapshot;
        Check(completed.history.Count==1&&completed.experience==25&&completed.fishCoins==10&&completed.tickets==0,"Medium task reward or duplicate changed");
        Check(completed.history.Single().experience==25&&completed.history.Single().fishCoins==10&&completed.history.Single().tickets==0,"Task receipt must match credited balances");
        store.Data.experience=99;Check(!service.CanAccess(ProgressionFeature.Focus),"Focus early unlock");
        store.Data.experience=100;Check(service.CanAccess(ProgressionFeature.Focus)&&!service.CanAccess(ProgressionFeature.Boss),"Lv.2 boundary");
        Check(service.BeginFocus(out var session),service.LastError);
        store.Data.experience=0; // Existing live session remains settleable after gate changes.
        store.fail=true;Check(!service.FinishFocus(session,1500,true)&&service.Snapshot.focusSessions.Single().status==FocusSessionStatus.Running,"Failed settlement committed");
        store.fail=false;Check(service.FinishFocus(session,1500,true)&&service.FinishFocus(session,1500,true),service.LastError);
        // Entry is Lv.2: floor(25 * 11 / 20)=13 EXP, floor(5 * 11 / 10)=5 Fish.
        // Lowering current XP after entry must not change the saved reward level.
        var settled=service.Snapshot;var receipt=settled.focusSessions.Single();
        Check(receipt.rewardLevel==2&&receipt.status==FocusSessionStatus.EndedEarly&&receipt.experience==13&&receipt.fishCoins==5&&receipt.tickets==1,"Saved entry level/End Early rewards changed");
        Check(settled.experience==13&&settled.fishCoins==15&&settled.tickets==1,"Focus retry credited rewards twice or changed totals");
        Check(!service.BeginFocus(out _),"Low-level continuation allowed new session");
        store.Data.experience=549;Check(!service.CanAccess(ProgressionFeature.Boss)&&!service.CanAccess(ProgressionFeature.Leaderboard),"Lv.5 early unlock");
        store.Data.experience=550;store.Data.tickets=3;
        Check(service.CanAccess(ProgressionFeature.Boss)&&service.CanAccess(ProgressionFeature.Leaderboard),"Lv.5 boundary");
        Check(service.BeginBattle(1,0,out var battle)&&store.Data.tickets==2,"Boss entry did not consume one ticket");
        Check(!service.BeginBattle(1,0,out _),"Duplicate battle entry");
        store.Data.experience=0;Check(service.ResolveBattle(battle.id,BattleOutcome.Error)&&service.ResolveBattle(battle.id,BattleOutcome.Error)&&store.Data.tickets==3,"Interrupted entry recovery duplicated");
        store.Data.experience=550;Check(service.BattleEntryError(2)!=null,"Boss 2 previous-win gate lost");
        store.Data.bossProgress.Single(b=>b.bossId==1).wins=1;Check(service.BattleEntryError(2)!=null,"Boss 2 Lv.10 gate lost");
        var folder="Library/TutorialRules/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);
        string file=folder+"/save.json";File.WriteAllText(file,JsonUtility.ToJson(new PlayerData{username="Existing",experience=550,tickets=8}));
        var disk=new PlayerDataStore(file);var persisted=new TaskService(disk,new Clock());Check(persisted.InitializeTutorial(),persisted.LastError);
        Check(File.Exists(file+".before-tutorial.bak"),"Missing pre-tutorial backup");
        var skipped=persisted.Tutorial;skipped.dismissed=true;skipped.focusStep=3;skipped.taskPageStep=7;skipped.bossStep=4;skipped.leaderboardStep=2;Check(persisted.SaveTutorial(skipped),persisted.LastError);
        var reload=new TaskService(new PlayerDataStore(file),new Clock());
        Check(reload.Tutorial.dismissed&&reload.Tutorial.focusStep==3&&reload.Tutorial.taskPageStep==7&&reload.Tutorial.bossStep==4&&reload.Tutorial.leaderboardStep==2&&reload.Snapshot.tickets==8&&reload.Snapshot.experience==550,"Reload changed state");
        var fresh=new PlayerDataStore(folder+"/new.json");Check(fresh.Data.tutorial.taskStep==0,"New player welcome missing");
        TaskSystemVerification.Run();
        File.WriteAllText("Temp/tutorial-rules.result",DateTime.UtcNow.ToString("O")+" PASS: level boundaries, direct entry guards, failure/retry, atomic tutorial/task receipt, unchanged task rewards, duplicate settlement, legacy continuation, recovery, boss prerequisites, reload, backup, existing TaskSystemVerification.");
    }
}
