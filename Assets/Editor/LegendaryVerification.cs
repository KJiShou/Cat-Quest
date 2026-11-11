using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LegendaryVerification
{
    sealed class Memory:IPlayerDataStore {public PlayerData Data{get;private set;}=new PlayerData();public bool fail;public bool TrySave(PlayerData d,out string e){e=fail?"Disk unavailable":null;if(fail)return false;PlayerDataStore.Normalize(d);Data=d;return true;}}
    static LegendaryVerification(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-legendary.request"))return;File.Delete("Temp/verify-legendary.request");try{Run();File.WriteAllText("Temp/verify-legendary.result",DateTime.UtcNow.ToString("O")+" PASS: six legendary abilities; Auto Select; crown/helm equip/unequip/scaling; exact staff cadence and piercing; crit/block; deterministic frame partitions; saved ability version and equipment snapshot; failure/retry, duplicate settlement and reload; existing Equipment aggregate.");}catch(Exception e){File.WriteAllText("Temp/verify-legendary.result",e.ToString());Debug.LogException(e);}}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static EquipmentDefinition Item(string icon)=>LootRules.Catalog.Single(d=>d.IconName==icon);
    static void Near(double a,double b,string why)=>Check(Math.Abs(a-b)<1e-8,why);
    public static void Run(){
        EquipmentVerification.Run();
        var crown=Item("Armor_MagicCrown");var armour=Item("Armor_HeavyArmor");var fist=Item("Armor_GauntletsGold");
        var m=new Memory();var service=new TaskService(m,new SystemTaskClock());m.Data.equipmentUnlocked=true;m.Data.experience=2025;m.Data.tickets=5;
        foreach(var def in new[]{crown,armour,fist})m.Data.equipment.Add(new EquipmentItem{id=def.Id});
        Near(service.CatStats.aspd,1,"Owned crown alone grants no haste");Check(service.Equip(crown.Id,EquipmentSlot.Head),"Equip crown");Near(service.CatStats.aspd,1.15*1.1,"10% multiplies total speed");
        m.Data.equipment.Single(i=>i.id==crown.Id).level=10;Near(service.CatStats.aspd,1.24*1.1,"Upgrade changes flat speed, not passive percentage");
        service.Unequip(EquipmentSlot.Head);Near(service.CatStats.aspd,1,"Unequip removes haste");
        foreach(var def in new[]{crown,armour,fist})Check(service.Equip(def.Id,def.Slot),"Equip passive item");Check(service.EquipmentStats.SetPieces.All(n=>n==0),"Legendary rarity does not grant Boss set");
        var fixture=new BattleAttempt{equipmentAbilityVersion=1,cat=new CombatStats(1000000,10,4,100),boss=new CombatStats(1000000,10,4,100),equippedEquipment=new List<EquipmentItem>{new EquipmentItem{id=fist.Id},new EquipmentItem{id=armour.Id}}};
        var hits=new List<BattleHit>();var sim=new BattleSimulation(fixture);sim.Advance(120,hits.Add);
        int attacks=hits.Count(h=>h.catAttacks),received=hits.Count-attacks,crits=hits.Count(h=>h.critical),blocks=hits.Count(h=>h.blocked);
        Check(crits>attacks*.18&&crits<attacks*.22&&blocks>received*.08&&blocks<received*.12,"20% crit / 10% block sample bounds");
        Check(hits.All(h=>h.damage==(h.blocked?0:h.critical?16:8)),"Crit doubles post-defence damage; block cancels whole hit");
        foreach(double delta in new[]{.007,.1,1.7}){
            var chunked=new BattleSimulation(fixture);var other=new List<BattleHit>();while(chunked.Outcome==BattleOutcome.Running)chunked.Advance(delta,other.Add);
            Check(hits.Count==other.Count&&hits.Zip(other,(a,b)=>a.damage==b.damage&&a.catHp==b.catHp&&a.bossHp==b.bossHp&&a.critical==b.critical&&a.blocked==b.blocked).All(x=>x),"Frame-independent passive sequence");
        }
        fixture.equipmentAbilityVersion=0;var legacy=new BattleSimulation(fixture);legacy.Advance(120,h=>Check(!h.critical&&!h.blocked,"Older snapshots retain original rules"));
        fixture.equipmentAbilityVersion=1;fixture.equippedEquipment.Clear();new BattleSimulation(fixture).Advance(120,h=>Check(h.damage==8&&!h.critical&&!h.blocked,"Unworn gear has no procs"));
        m.fail=true;Check(!service.BeginBattle(1,0,out _)&&m.Data.tickets==5,"Failed entry spends nothing");m.fail=false;
        Check(service.BeginBattle(1,0,out var attempt)&&attempt.equipmentAbilityVersion==2,"Versioned ability snapshot");
        var expected=new BattleSimulation(attempt);expected.Advance(120);
        Check(!service.Unequip(EquipmentSlot.Body),"Cannot change abilities mid-battle");
        var path=Path.GetFullPath("Library/LegendaryVerification/"+Guid.NewGuid().ToString("N")+".json");var disk=new PlayerDataStore(path);Check(disk.TrySave(service.Snapshot,out _),"Persist ability snapshot");
        var reload=new PlayerDataStore(path).Data.battles.Single(b=>b.id==attempt.id);var replay=new BattleSimulation(reload);replay.Advance(120);Check(replay.CatHp==expected.CatHp&&replay.BossHp==expected.BossHp&&replay.Outcome==expected.Outcome,"Reload reproduces exact outcome");
        m.fail=true;Check(!service.ResolveBattle(attempt.id,BattleOutcome.Victory)&&service.HasActiveBattle,"Settlement failure retains pending entry");m.fail=false;
        Check(service.ResolveBattle(attempt.id,BattleOutcome.Victory)&&m.Data.battles.Last().outcome==expected.Outcome,"Authoritative replay uses abilities");
        int coins=service.FishCoins;Check(service.ResolveBattle(attempt.id,BattleOutcome.Victory)&&service.FishCoins==coins,"Duplicate settlement grants nothing");
        Check(service.BeginBattle(1,0,out var cancelled)&&service.ResolveBattle(cancelled.id,BattleOutcome.Stopped),"Cancellation supported");
        RemainingAbilities();AutoSelection();
    }
    static void RemainingAbilities(){
        var helm=Item("Armor_HelmGolden");var sword=Item("Weapon_ShortSword");var staff=Item("Weapon_MagicStaff_01");
        var m=new Memory();var s=new TaskService(m,new SystemTaskClock());m.Data.experience=2025;m.Data.equipmentUnlocked=true;m.Data.tickets=8;
        foreach(var def in new[]{helm,sword,staff})m.Data.equipment.Add(new EquipmentItem{id=def.Id});
        Check(s.CatStats.hp==190,"Owned helm does not grant HP");s.Equip(helm.Id,EquipmentSlot.Head);Check(s.CatStats.hp==219,"Helm multiplies max HP and rounds half up");
        m.Data.equipment.Single(i=>i.id==helm.Id).level=10;Check(s.CatStats.hp==219,"Helm percentage does not scale with level");s.Unequip(EquipmentSlot.Head);Check(s.CatStats.hp==190,"Unequip removes HP");
        foreach(var weapon in new[]{sword,staff}){
            var a=new BattleAttempt{equipmentAbilityVersion=2,cat=new CombatStats(10000,20,0,2),boss=new CombatStats(10000,1,20,1),equippedEquipment=new List<EquipmentItem>{new EquipmentItem{id=weapon.Id}}};
            var hits=new List<BattleHit>();new BattleSimulation(a).Advance(5,hits.Add);var outgoing=hits.Where(h=>h.catAttacks).ToArray();
            Check(outgoing.Length==10,"Ten attacks at boundary");
            for(int i=0;i<10;i++)Check(outgoing[i].damage==(weapon==sword?12:(i+1)%5==0?20:10)&&outgoing[i].critical==(weapon==staff&&(i+1)%5==0),"Pierce and exact fifth/tenth attack burst");
            var split=new List<BattleHit>();var sim=new BattleSimulation(a);for(int i=0;i<50;i++)sim.Advance(.1,split.Add);Check(hits.Select(h=>h.damage).SequenceEqual(split.Select(h=>h.damage)),"Staff counter independent of frames");
            var restarted=new List<BattleHit>();new BattleSimulation(a).Advance(.5,restarted.Add);Check(!restarted[0].critical,"Staff starts a fresh count each battle");
            a.equipmentAbilityVersion=1;new BattleSimulation(a).Advance(5,h=>{if(h.catAttacks)Check(h.damage==10&&!h.critical,"Version 1 retains pre-pierce/burst rules");});
            s.Equip(weapon.Id,EquipmentSlot.Weapon);s.Equip(helm.Id,EquipmentSlot.Head);Check(s.BeginBattle(1,0,out var entry),"New weapon entry");
            var path=Path.GetFullPath("Library/LegendaryVerification/"+Guid.NewGuid().ToString("N")+".json");var disk=new PlayerDataStore(path);Check(disk.TrySave(s.Snapshot,out _),"New weapon snapshot save");var saved=new PlayerDataStore(path).Data;
            var before=new BattleSimulation(entry);before.Advance(120);var after=new BattleSimulation(saved.battles.Last());after.Advance(120);Check(before.CatHp==after.CatHp&&before.BossHp==after.BossHp,"New weapon reload");
            Check(s.ResolveBattle(entry.id,BattleOutcome.Victory)&&s.Snapshot.battles.Last().outcome==before.Outcome,"New weapon authoritative settlement");
        }
    }
    static void AutoSelection(){
        foreach(string icon in new[]{"Armor_HelmGolden","Armor_HeavyArmor","Armor_GauntletsGold","Armor_MagicCrown","Weapon_MagicStaff_01","Weapon_ShortSword"}){
            var def=Item(icon);var m=new Memory();var s=new TaskService(m,new SystemTaskClock());m.Data.experience=2025;m.Data.equipmentUnlocked=true;
            var plain=EquipmentRules.Catalog.Single(d=>d.Tier==3&&d.Slot==def.Slot);
            m.Data.equipment.Add(new EquipmentItem{id=plain.Id});m.Data.equipment.Add(new EquipmentItem{id=def.Id});s.Equip(plain.Id,plain.Slot);
            Check(s.AutoEquip()&&s.EquippedItem(def.Slot)==def.Id,"Auto Select values "+icon);
            var selected=s.EquippedItem(def.Slot);Check(s.AutoEquip()&&s.EquippedItem(def.Slot)==selected,"Repeat Auto Select stable");
            m.fail=true;Check(!s.AutoEquip()&&s.EquippedItem(def.Slot)==selected,"Failed Auto Select preserves gear");
        }
        var data=new PlayerData{experience=2025};var fist=Item("Armor_GauntletsGold");var staff=Item("Weapon_MagicStaff_01");data.equipment.Add(new EquipmentItem{id=fist.Id});data.equipment.Add(new EquipmentItem{id=staff.Id});
        var fatal=new CombatStats(10000,1000,0,1);EquipmentRules.SetEquipped(data,EquipmentSlot.Weapon,staff.Id);double shortStaff=EquipmentRules.AutoEquipScore(data,fatal);EquipmentRules.SetEquipped(data,EquipmentSlot.Weapon,fist.Id);Check(EquipmentRules.AutoEquipScore(data,fatal)>shortStaff,"Short fight does not assume fifth attack burst");
    }
}
