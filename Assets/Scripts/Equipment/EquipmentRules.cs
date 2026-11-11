using System;
using System.Collections.Generic;
using System.Linq;

public enum EquipmentSlot { Weapon, Body, Head }
[Serializable] public sealed class EquipmentItem { public string id; public int level=1; }
[Serializable] public sealed class EquipmentUpgradeReceipt { public string id,itemId;public int fromLevel; }
public sealed class EquipmentDefinition
{
    public readonly string Id,Name;public readonly int Tier;public readonly EquipmentSlot Slot;
    public readonly string IconName;
    public bool IsLoot => IconName != null;
    public EquipmentDefinition(string id,string name,int tier,EquipmentSlot slot,string iconName){Id=id;Name=name;Tier=tier;Slot=slot;IconName=iconName;}
    public EquipmentDefinition(int tier,EquipmentSlot slot){Tier=tier;Slot=slot;Id=$"boss{tier}-{slot.ToString().ToLowerInvariant()}";Name=$"{EquipmentRules.BossName(tier)} {EquipmentRules.SlotName(slot)}";}
}
public sealed class EquipmentStats
{
    public CombatStats Base,Final;
    public double GearAtk,GearDef,GearAspd,SetAtk,SetDef,SetAspd;
    public int SetHp;
    public int[] SetPieces=new int[3];
}
public enum EquipmentAbility { None, CrownHaste, ArmourGuard, GauntletCritical, HelmVitality, SwordPiercing, StaffBurst }
public static class EquipmentRules
{
    public static readonly EquipmentDefinition[] Catalog=Enumerable.Range(1,3).SelectMany(t=>Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>().Select(s=>new EquipmentDefinition(t,s))).ToArray();
    public static string BossName(int tier)=>tier==1?"Cyclope":tier==2?"Orc":"Demon";
    public static string SlotName(EquipmentSlot slot)=>slot==EquipmentSlot.Weapon?"Weapon":slot==EquipmentSlot.Body?"Armour":"Head Accessory";
    public static EquipmentDefinition Find(string id)=>Catalog.FirstOrDefault(d=>d.Id==id) ?? LootRules.Catalog.FirstOrDefault(d=>d.Id==id);
    public static EquipmentAbility Ability(EquipmentDefinition d)=>d?.IconName switch {
        "Armor_MagicCrown"=>EquipmentAbility.CrownHaste,
        "Armor_HeavyArmor"=>EquipmentAbility.ArmourGuard,
        "Armor_GauntletsGold"=>EquipmentAbility.GauntletCritical,
        "Armor_HelmGolden"=>EquipmentAbility.HelmVitality,
        "Weapon_ShortSword"=>EquipmentAbility.SwordPiercing,
        "Weapon_MagicStaff_01"=>EquipmentAbility.StaffBurst,
        _=>EquipmentAbility.None
    };
    public static EquipmentAbility EquippedAbility(PlayerData d,EquipmentSlot slot) {
        var id=Equipped(d,slot);var def=Find(id);
        return def?.Slot==slot&&d.equipment?.Any(i=>i.id==id)==true?Ability(def):EquipmentAbility.None;
    }
    public static string AbilityDescription(EquipmentDefinition d)=>Ability(d) switch {
        EquipmentAbility.CrownHaste=>"+10% total attack speed",
        EquipmentAbility.ArmourGuard=>"10% chance to block all hit damage",
        EquipmentAbility.GauntletCritical=>"20% chance to deal double damage",
        EquipmentAbility.HelmVitality=>"+15% maximum health",
        EquipmentAbility.SwordPiercing=>"Ignore 20% of enemy defence",
        EquipmentAbility.StaffBurst=>"Every 5th attack deals double damage",
        _=>""
    };
    public static double Bonus(EquipmentDefinition d,int level)=>d.Slot==EquipmentSlot.Head?.05*d.Tier+.01*(level-1):2*d.Tier+.5*(level-1);
    public static int UpgradeCost(int level)=>checked(10+5*level);
    public static string Effect(EquipmentDefinition d,int level)=>d.Slot==EquipmentSlot.Head?$"+{Bonus(d,level):0.00} ASPD":$"+{Bonus(d,level):0.#} {(d.Slot==EquipmentSlot.Weapon?"ATK":"DEF")}";
    public static string Equipped(PlayerData d,EquipmentSlot slot)=>slot==EquipmentSlot.Weapon?d.equippedWeaponId:slot==EquipmentSlot.Body?d.equippedBodyOutfitId:d.equippedHeadAccessoryId;
    public static void SetEquipped(PlayerData d,EquipmentSlot slot,string id){if(slot==EquipmentSlot.Weapon)d.equippedWeaponId=id;else if(slot==EquipmentSlot.Body)d.equippedBodyOutfitId=id;else d.equippedHeadAccessoryId=id;}
    public static EquipmentStats Calculate(PlayerData d)
    {
        var result=new EquipmentStats{Base=BattleRules.Cat(d.experience)};
        foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot))){
            var item=d.equipment?.FirstOrDefault(i=>i.id==Equipped(d,slot));var def=Find(item?.id);
            if(def==null||def.Slot!=slot)continue;
            double bonus=Bonus(def,item.level);if(!def.IsLoot)result.SetPieces[def.Tier-1]++;
            if(slot==EquipmentSlot.Weapon)result.GearAtk+=bonus;else if(slot==EquipmentSlot.Body)result.GearDef+=bonus;else result.GearAspd+=bonus;
        }
        for(int i=0;i<3;i++){
            if(result.SetPieces[i]>=2)result.SetHp=BattleRules.Round(result.Base.hp*(100+5*(i+1))/100.0)-result.Base.hp;
            if(result.SetPieces[i]==3){result.SetAtk=i+1;result.SetDef=i+1;result.SetAspd=.02*(i+1);}
        }
        result.Final=new CombatStats(result.Base.hp+result.SetHp,result.Base.atk+result.GearAtk+result.SetAtk,result.Base.def+result.GearDef+result.SetDef,result.Base.aspd+result.GearAspd+result.SetAspd);
        if(EquippedAbility(d,EquipmentSlot.Head)==EquipmentAbility.CrownHaste)
            result.Final.aspd=Math.Min(100,result.Final.aspd*1.1);
        if(EquippedAbility(d,EquipmentSlot.Head)==EquipmentAbility.HelmVitality)
            result.Final.hp=BattleRules.Round(result.Final.hp*115.0/100.0);
        return result;
    }
    // Expected damage over the survival window, not a search for a lucky RNG seed.
    // Includes full-set stats; short windows do not assume a staff proc before attack five.
    public static double AutoEquipScore(PlayerData d,CombatStats boss) {
        var cat=Calculate(d).Final;var weapon=EquippedAbility(d,EquipmentSlot.Weapon);
        var defence=boss;if(weapon==EquipmentAbility.SwordPiercing)defence.def*=.8;
        double damage=BattleRules.Damage(cat,defence);
        double incoming=BattleRules.Damage(boss,cat)*boss.aspd*(EquippedAbility(d,EquipmentSlot.Body)==EquipmentAbility.ArmourGuard?.9:1);
        double attacks=Math.Min(120,cat.hp/incoming)*cat.aspd;
        double outgoing=damage*(weapon==EquipmentAbility.StaffBurst?attacks+Math.Floor(attacks/5):attacks*(weapon==EquipmentAbility.GauntletCritical?1.2:1));
        return outgoing/boss.hp;
    }
    public static void Normalize(PlayerData d)
    {
        d.equipment??=new List<EquipmentItem>();d.equipmentUpgrades??=new List<EquipmentUpgradeReceipt>();
        d.equipment=d.equipment.Where(i=>i!=null&&Find(i.id)!=null).GroupBy(i=>i.id).Select(g=>g.OrderByDescending(i=>i.level).First()).ToList();
        TaskService.CatLevel(d.experience,out int level,out _,out _);
        foreach(var item in d.equipment)item.level=Math.Max(1,Math.Min(level,item.level));
        d.equipmentUpgrades.RemoveAll(r=>r==null||string.IsNullOrEmpty(r.id));
        foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot))){var id=Equipped(d,slot);if(!d.equipment.Any(i=>i.id==id&&Find(id).Slot==slot))SetEquipped(d,slot,null);}
        foreach(var p in d.bossProgress){p.claimedComponents=d.equipment.Count(i=>!Find(i.id).IsLoot&&Find(i.id).Tier==p.bossId);p.pendingClaims=Math.Max(0,Math.Min(3,p.wins/5)-p.claimedComponents);}
        d.equipmentUnlocked=d.equipmentUnlocked||d.equipment.Count>0||d.rewardDays.Any(r=>r.questsClaimed||r.focusClaimed);
    }
}
