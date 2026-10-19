using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BattleVerification
{
    sealed class Memory : IPlayerDataStore
    {
        public PlayerData Data {get;private set;} = new PlayerData();
        public bool fail;
        public bool TrySave(PlayerData candidate,out string error){error=fail?"Simulated disk failure":null;if(fail)return false;Data=candidate;return true;}
    }
    static BattleVerification(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-battle-rules.request"))return;
        File.Delete("Temp/verify-battle-rules.request");
        try{Run();File.WriteAllText("Temp/verify-battle-rules.result","PASS: stats, rounding, attack timing, deterministic frame rates, tie priority, timeout, gates, atomic entry, settlement retries, first-clear/mastery/master rewards, stop, recovery and migration.");}
        catch(Exception e){File.WriteAllText("Temp/verify-battle-rules.result",e.ToString());Debug.LogException(e);}
    }
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static int Xp(int level){int x=0;for(int l=1;l<level;l++)x+=100+25*(l-1);return x;}
    public static void Run()
    {
        FocusImplementationVerification.Run();
        var c=BattleRules.Cat(0);Check(c.hp==100&&c.atk==5&&c.def==5&&c.aspd==1&&c.Power==290,"Level 1");
        c=BattleRules.Cat(Xp(20));Check(c.hp==290&&c.atk==24&&c.def==24&&c.aspd==1,"Level 20");
        Check(BattleRules.Damage(new CombatStats(1,5,0,1),new CombatStats(1,0,5,1))==3,"Half damage rounding");
        Check(BattleRules.Damage(new CombatStats(1,0,0,1),new CombatStats(1,0,100,1))==1,"Minimum damage");
        var tie=new BattleSimulation(new CombatStats(1,10,0,1),new CombatStats(1,10,0,1));tie.Advance(1);Check(tie.Outcome==BattleOutcome.Victory&&tie.CatHp==1,"Cat wins lethal tie");
        var slow=new BattleSimulation(new CombatStats(10000,1,0,1),new CombatStats(10000,1,0,1));slow.Advance(120);Check(slow.Outcome==BattleOutcome.Error,"120s safety");
        foreach(double delta in new[]{1.0/30,1.0/144,.7,120.0}){
            var sim=new BattleSimulation(BattleRules.Cat(Xp(10)),BattleRules.Boss(2));while(sim.Outcome==BattleOutcome.Running)sim.Advance(delta);
            Check(sim.Outcome==BattleOutcome.Defeat&&sim.CatHp==0,"Level 10 without equipment loses to Boss 2");
            var reference=new BattleSimulation(BattleRules.Cat(Xp(10)),BattleRules.Boss(2));reference.Advance(120);
            Check(sim.CatHp==reference.CatHp&&Math.Abs(sim.Elapsed-reference.Elapsed)<1e-7,"Frame-rate deterministic HP/time");
        }
        var fast=new BattleSimulation(new CombatStats(10000,1,0,2),new CombatStats(10000,1,0,1));int hits=0;fast.Advance(5,h=>{if(h.catAttacks)hits++;});Check(hits==10,"2 ASPD = 10 attacks in 5s");
        var m=new Memory();var service=new TaskService(m,new SystemTaskClock());
        Check(!service.BeginBattle(1,0,out _),"No tickets");m.Data.tickets=50;
        Check(!service.BeginBattle(2,0,out _),"Boss 2 gate");
        m.fail=true;Check(!service.BeginBattle(1,0,out _)&&m.Data.tickets==50&&m.Data.battles.Count==0,"Atomic entry failure");m.fail=false;
        m.Data.experience=Xp(30);
        Check(service.BeginBattle(1,0,out var a),"Entry");Check(!service.BeginBattle(1,0,out _),"Duplicate entry");
        m.fail=true;Check(!service.ResolveBattle(a.id,BattleOutcome.Victory)&&m.Data.bossProgress[0].wins==0,"Atomic result failure");m.fail=false;
        int xp=m.Data.experience;
        Check(service.ResolveBattle(a.id,BattleOutcome.Victory)&&service.ResolveBattle(a.id,BattleOutcome.Victory),"Retry settlement");
        Check(m.Data.experience==xp+30&&m.Data.bossProgress[0].wins==1&&m.Data.tickets==49,"Single first-clear reward");
        for(int i=1;i<15;i++){Check(service.BeginBattle(1,0,out a),"Replay entry");Check(service.ResolveBattle(a.id,BattleOutcome.Victory),"Replay settle");}
        Check(m.Data.bossProgress[0].pendingClaims==3&&m.Data.bossProgress[0].wins==15&&m.Data.fishCoins==0&&m.Data.experience==xp+30,"Milestones stored, equipment deferred, no repeat EXP");
        Check(service.BeginBattle(1,0,out a),"Stop entry");int tickets=m.Data.tickets;service.ResolveBattle(a.id,BattleOutcome.Stopped);Check(m.Data.tickets==tickets&&m.Data.bossProgress[0].wins==15,"Stop consumes ticket");
        Check(service.BeginBattle(1,0,out a),"Recovery entry");tickets=m.Data.tickets;service=new TaskService(m,new SystemTaskClock());Check(m.Data.tickets==tickets+1,"Recovery refunds");new TaskService(m,new SystemTaskClock());Check(m.Data.tickets==tickets+1,"Recovery exactly once");
        m.Data.experience=0;Check(service.BattleEntryError(2).Contains("10"),"Level gate despite clear");m.Data.experience=Xp(100);
        foreach(int id in new[]{2,3}){Check(service.BeginBattle(id,0,out a),"Next boss");Check(service.ResolveBattle(a.id,BattleOutcome.Victory),"Next victory");}
        Check(service.BeginBattle(1,1,out a),"Master entry");xp=m.Data.experience;int coins=m.Data.fishCoins;service.ResolveBattle(a.id,BattleOutcome.Victory);
        Check(m.Data.masterStage==2&&m.Data.experience==xp+60&&m.Data.fishCoins==coins+30,"Master first reward");
        Check(!service.BeginBattle(1,1,out _),"Master stage cannot repay");
        var ms=BattleRules.Boss(1,4);Check(ms.hp==600&&ms.atk==40&&ms.def==22&&Math.Abs(ms.aspd-1.12)<1e-8,"Master scaling");
        var old=JsonUtility.FromJson<PlayerData>("{\"version\":3,\"username\":\"Existing\",\"experience\":123,\"tickets\":4}");PlayerDataStore.Normalize(old);
        Check(old.version==4&&old.experience==123&&old.tickets==4&&old.bossProgress.Count==3&&old.masterStage==1,"Old save migration");
    }
}
