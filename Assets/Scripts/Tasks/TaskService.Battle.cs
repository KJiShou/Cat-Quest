using System;
using System.Linq;

public sealed partial class TaskService
{
    public CombatStats CatStats => BattleRules.Cat(store.Data.experience);
    public string BattleEntryError(int bossId,int masterStage=0)
    {
        var d=store.Data;
        if(d.battles.Any(b=>b.outcome==BattleOutcome.Running)) return "Finish the current battle first.";
        if(bossId<1||bossId>3||masterStage<0) return "Invalid boss.";
        int Wins(int id)=>d.bossProgress.FirstOrDefault(p=>p.bossId==id)?.wins??0;
        CatLevel(d.experience,out int level,out _,out _);
        if(masterStage>0) {
            if(Wins(3)==0) return "Defeat Boss 3 to unlock Master.";
            if(masterStage!=d.masterStage || bossId!=1+(masterStage-1)%3) return "This Master stage is not available.";
        } else {
            if(bossId>1 && Wins(bossId-1)==0) return "Defeat Boss "+(bossId-1)+" first.";
            int required=bossId==1?1:bossId==2?10:20;
            if(level<required) return "Requires Lv. "+required+".";
        }
        return d.tickets<1 ? "Earn a ticket through Focus." : null;
    }
    public bool BeginBattle(int bossId,int masterStage,out BattleAttempt attempt)
    {
        attempt=null;
        var error=BattleEntryError(bossId,masterStage);
        if(error!=null) {LastError=error;return false;}
        var item=new BattleAttempt {id=Guid.NewGuid().ToString("N"),bossId=bossId,masterStage=masterStage,cat=CatStats,boss=BattleRules.Boss(bossId,masterStage),startedUtc=clock.UtcNow.ToString("o")};
        if(!Commit(d=>{d.tickets--;d.battles.Add(item);})) return false;
        attempt=PlayerDataStore.Copy(item);return true;
    }
    // Recompute from the saved snapshot; callers cannot award a fabricated victory.
    public bool ResolveBattle(string id,BattleOutcome request)
    {
        var saved=store.Data.battles.FirstOrDefault(b=>b.id==id);
        if(saved==null) {LastError="Battle not found.";return false;}
        if(saved.outcome!=BattleOutcome.Running) return true;
        BattleOutcome outcome=request;
        if(request!=BattleOutcome.Stopped && request!=BattleOutcome.Error) {
            var sim=new BattleSimulation(saved.cat,saved.boss);sim.Advance(120);outcome=sim.Outcome;
        }
        return Commit(d=>{
            var b=d.battles.Single(x=>x.id==id);b.outcome=outcome;b.completedUtc=clock.UtcNow.ToString("o");
            if(outcome==BattleOutcome.Error) {d.tickets=checked(d.tickets+1);return;}
            if(outcome!=BattleOutcome.Victory) return;
            if(b.masterStage>0) {
                if(b.masterStage!=d.masterStage) throw new ArgumentException("Master stage has already changed.");
                b.experience=checked(50+10*b.masterStage);b.fishCoins=checked(25+5*b.masterStage);d.masterStage=checked(d.masterStage+1);
            } else {
                var p=d.bossProgress.Single(x=>x.bossId==b.bossId);
                bool first=p.wins==0;
                p.wins=checked(p.wins+1);
                bool milestone=p.wins==5||p.wins==10||p.wins==15;
                if(first) b.experience=b.bossId==1?30:b.bossId==2?60:100;
                if(milestone) p.pendingClaims++;
                else if(p.claimedComponents==3) b.fishCoins=10*b.bossId;
            }
            d.experience=checked(d.experience+b.experience);d.fishCoins=checked(d.fishCoins+b.fishCoins);
        });
    }
    public bool RecoverInterruptedBattles()
    {
        if(!store.Data.battles.Any(b=>b.outcome==BattleOutcome.Running)) return true;
        return Commit(d=>{
            foreach(var b in d.battles.Where(x=>x.outcome==BattleOutcome.Running)) {
                b.outcome=BattleOutcome.Error;b.completedUtc=clock.UtcNow.ToString("o");d.tickets=checked(d.tickets+1);
            }
        });
    }
}
