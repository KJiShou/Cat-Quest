using System;
using System.Collections.Generic;

[Serializable]
public struct CombatStats
{
    public int hp;
    public double atk, def, aspd;
    public CombatStats(int hp, double atk, double def, double aspd) { this.hp=hp;this.atk=atk;this.def=def;this.aspd=aspd; }
    public int Power => BattleRules.Round(hp+10*atk+8*def+100*aspd);
    public bool Valid => hp>0 && atk>=0 && def>=0 && aspd>0 && aspd<=100 &&
        !double.IsNaN(atk+def+aspd) && !double.IsInfinity(atk+def+aspd);
}
public enum BattleOutcome { Running, Victory, Defeat, Stopped, Error }
[Serializable]
public sealed class BossProgress
{
    public int bossId, wins, pendingClaims, claimedComponents;
}
[Serializable]
public sealed class BattleAttempt
{
    public string id, startedUtc, completedUtc;
    public int bossId, masterStage, experience, fishCoins;
    public CombatStats cat, boss;
    public List<EquipmentItem> equippedEquipment = new List<EquipmentItem>();
    public BattleOutcome outcome;
}
public static class BattleRules
{
    // The document leaves .5 ties unspecified: consistently round halves upward.
    public static int Round(double value) => checked((int)Math.Round(value,MidpointRounding.AwayFromZero));
    public static CombatStats Cat(int experience)
    {
        TaskService.CatLevel(experience,out int level,out _,out _);
        return new CombatStats(100+10*(level-1),5+level-1,5+level-1,1);
    }
    public static CombatStats Boss(int id,int master=0)
    {
        if(id<1||id>3||master<0) throw new ArgumentException("Invalid boss or stage.");
        if(master==0) return id==1 ? new CombatStats(90,8,5,.9) : id==2 ? new CombatStats(220,18,10,1) : new CombatStats(450,35,18,1.1);
        if(id!=1+(master-1)%3) throw new ArgumentException("Master boss does not match stage.");
        int cycle=(master-1)/3;
        return new CombatStats(Round((450+50*id)*(1+.2*cycle)),Round((34+2*id)*(1+.1*cycle)),Round((19+id)*(1+.1*cycle)),1.08+.02*id+.02*cycle);
    }
    public static int Damage(CombatStats attacker,CombatStats defender) => Math.Max(1,Round(attacker.atk-.5*defender.def));
}
public readonly struct BattleHit
{
    public readonly bool catAttacks;
    public readonly int damage, catHp, bossHp;
    public readonly double time;
    public BattleHit(bool cat,int damage,int catHp,int bossHp,double time) {catAttacks=cat;this.damage=damage;this.catHp=catHp;this.bossHp=bossHp;this.time=time;}
}
// Integer attack counters avoid cumulative timer drift. Rendering never decides damage.
public sealed class BattleSimulation
{
    public readonly CombatStats Cat, Boss;
    public int CatHp {get;private set;}
    public int BossHp {get;private set;}
    public double Elapsed {get;private set;}
    public BattleOutcome Outcome {get;private set;} = BattleOutcome.Running;
    int catCount=1,bossCount=1;
    public BattleSimulation(CombatStats cat,CombatStats boss)
    {
        Cat=cat;Boss=boss;CatHp=cat.hp;BossHp=boss.hp;
        if(!cat.Valid||!boss.Valid) Outcome=BattleOutcome.Error;
    }
    public void Advance(double delta,Action<BattleHit> hit=null)
    {
        if(Outcome!=BattleOutcome.Running) return;
        if(delta<0||double.IsNaN(delta)||double.IsInfinity(delta)) {Outcome=BattleOutcome.Error;return;}
        double end=Math.Min(120,Elapsed+delta);
        while(Outcome==BattleOutcome.Running) {
            double ct=catCount/Cat.aspd,bt=bossCount/Boss.aspd;
            bool cat=ct<=bt+1e-9;
            double t=cat?ct:bt;
            if(t>end+1e-9) break;
            int damage=cat?BattleRules.Damage(Cat,Boss):BattleRules.Damage(Boss,Cat);
            if(cat) {catCount++;BossHp=Math.Max(0,BossHp-damage);} else {bossCount++;CatHp=Math.Max(0,CatHp-damage);}
            Elapsed=t;
            if(BossHp==0) Outcome=BattleOutcome.Victory; else if(CatHp==0) Outcome=BattleOutcome.Defeat;
            hit?.Invoke(new BattleHit(cat,damage,CatHp,BossHp,t));
        }
        if(Outcome==BattleOutcome.Running) {Elapsed=end;if(end>=120) Outcome=BattleOutcome.Error;}
    }
}
