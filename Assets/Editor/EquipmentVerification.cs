using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EquipmentVerification
{
    sealed class Memory:IPlayerDataStore {public PlayerData Data{get;private set;}=new PlayerData();public bool fail;public bool TrySave(PlayerData d,out string e){e=fail?"Disk unavailable":null;if(fail)return false;PlayerDataStore.Normalize(d);Data=d;return true;}}
    static EquipmentVerification(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-equipment.request"))return;File.Delete("Temp/verify-equipment.request");try{Run();File.WriteAllText("Temp/verify-equipment.result","PASS: nine equipment definitions, all stat formulas and set tiers, mixed sets, mastery claims, upgrade costs/cap/funds, stale/duplicate requests, save failures, equipped battle snapshot and ASPD, post-set rewards, migration and reload. Existing battle/focus/task regressions passed.");}catch(Exception e){File.WriteAllText("Temp/verify-equipment.result",e.ToString());Debug.LogException(e);}}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Near(double a,double b,string why){Check(Math.Abs(a-b)<1e-8,why+": "+a+" != "+b);}
    public static void Run(){
        BattleVerification.Run();
        var m=new Memory();var s=new TaskService(m,new SystemTaskClock());
        Check(!s.ClaimEquipment("boss1-weapon"),"First daily reward gate");
        m.Data.rewardDays.Add(new RewardDay{day="2026-10-20",questsClaimed=true});m.Data.experience=2025;m.Data.fishCoins=1000;
        Check(!s.ClaimEquipment("boss1-weapon"),"No milestone");
        foreach(var p in m.Data.bossProgress){p.wins=15;p.pendingClaims=3;}
        m.fail=true;Check(!s.ClaimEquipment("boss1-weapon")&&m.Data.equipment.Count==0&&m.Data.bossProgress[0].pendingClaims==3,"Claim atomic failure");m.fail=false;
        foreach(var def in EquipmentRules.Catalog){Check(s.ClaimEquipment(def.Id),"Claim "+def.Id);Check(!s.ClaimEquipment(def.Id),"Duplicate claim");}
        Check(m.Data.equipment.Count==9&&m.Data.fishCoins==1000&&m.Data.bossProgress.All(p=>p.pendingClaims==0&&p.claimedComponents==3),"Three sets without coin grant");
        Check(!s.Equip("boss1-head",EquipmentSlot.Weapon)&&!s.Equip("bad",EquipmentSlot.Weapon),"Ownership and compatibility");
        foreach(int tier in new[]{1,2,3}){
            foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))s.Unequip(slot);
            s.Equip($"boss{tier}-weapon",EquipmentSlot.Weapon);s.Equip($"boss{tier}-body",EquipmentSlot.Body);
            Check(s.CatStats.hp==BattleRules.Round(190*(100+5*tier)/100.0),"Two piece HP");Near(s.CatStats.atk,14+2*tier,"Weapon formula");Near(s.CatStats.def,14+2*tier,"Body formula");Near(s.CatStats.aspd,1,"Two piece no speed");
            s.Equip($"boss{tier}-head",EquipmentSlot.Head);Near(s.CatStats.atk,14+3*tier,"Three piece attack");Near(s.CatStats.def,14+3*tier,"Three piece defence");Near(s.CatStats.aspd,1+.07*tier,"Three piece speed");
        }
        s.Equip("boss1-weapon",EquipmentSlot.Weapon);s.Equip("boss2-body",EquipmentSlot.Body);s.Equip("boss3-head",EquipmentSlot.Head);Check(s.CatStats.hp==190,"Mixed sets no bonus");
        m.fail=true;Check(!s.AutoEquip()&&m.Data.equippedWeaponId=="boss1-weapon","Auto equip atomic failure");m.fail=false;
        Check(s.AutoEquip()&&m.Data.equippedWeaponId=="boss3-weapon"&&m.Data.equippedBodyOutfitId=="boss3-body"&&m.Data.equippedHeadAccessoryId=="boss3-head","Auto equip includes set bonuses");
        s.Equip("boss1-weapon",EquipmentSlot.Weapon);s.Equip("boss2-body",EquipmentSlot.Body);
        m.fail=true;Check(!s.UpgradeEquipment("boss3-head",1,"retry")&&m.Data.fishCoins==1000&&m.Data.equipment.Single(x=>x.id=="boss3-head").level==1,"Upgrade atomic failure");m.fail=false;
        Check(s.UpgradeEquipment("boss3-head",1,"retry"),"Upgrade retry");Near(s.CatStats.aspd,1.16,"Upgraded head speed");Check(m.Data.fishCoins==985,"Exact 15 coin cost");
        Check(s.UpgradeEquipment("boss3-head",1,"retry")&&m.Data.fishCoins==985,"Idempotent upgrade");Check(!s.UpgradeEquipment("boss1-weapon",1,"retry"),"Request cannot change item");Check(!s.UpgradeEquipment("boss3-head",1,"stale"),"Stale level rejected");
        for(int level=2;level<10;level++)Check(s.UpgradeEquipment("boss3-head",level,"level"+level),"Upgrade level "+level);
        Check(!s.UpgradeEquipment("boss3-head",10,"over-cap"),"Cat level cap");
        int coins=m.Data.fishCoins;m.Data.fishCoins=0;Check(!s.UpgradeEquipment("boss1-body",1,"poor"),"Insufficient funds");m.Data.fishCoins=coins;
        m.fail=true;string before=m.Data.equippedWeaponId;Check(!s.Unequip(EquipmentSlot.Weapon)&&m.Data.equippedWeaponId==before,"Equip atomic failure");m.fail=false;
        m.Data.tickets=2;Check(s.BeginBattle(1,0,out var battle),"Equipped entry");Near(battle.cat.aspd,s.CatStats.aspd,"Final stats saved");Check(battle.equippedEquipment.Count==3&&battle.equippedEquipment.Single(x=>x.id=="boss3-head").level==10,"Equipment snapshot");
        Check(!s.AutoEquip()&&!s.Unequip(EquipmentSlot.Head)&&!s.UpgradeEquipment("boss1-body",1,"in-battle"),"Changes blocked during battle");
        var sim=new BattleSimulation(battle.cat,new CombatStats(10000,1,0,1));int hits=0;sim.Advance(5,h=>{if(h.catAttacks)hits++;});Check(hits==(int)Math.Floor(5*battle.cat.aspd),"Real ASPD attack intervals");
        coins=m.Data.fishCoins;Check(s.ResolveBattle(battle.id,BattleOutcome.Victory)&&m.Data.fishCoins==coins+10,"Post-set repeat reward");
        var path=Path.GetFullPath("Library/EquipmentVerification/save-"+Guid.NewGuid().ToString("N")+".json");var disk=new PlayerDataStore(path);Check(disk.TrySave(s.Snapshot,out _),"Disk save");var reloaded=new TaskService(new PlayerDataStore(path),new SystemTaskClock());Check(reloaded.Snapshot.equipment.Count==9&&reloaded.Snapshot.equipmentUpgrades.Count==9,"Reload inventory and receipts");Near(reloaded.CatStats.aspd,s.CatStats.aspd,"Reload derived stats");
        var legacy=JsonUtility.FromJson<PlayerData>("{\"version\":4,\"username\":\"Existing\",\"experience\":2025,\"fishCoins\":123,\"tickets\":7,\"bossProgress\":[{\"bossId\":1,\"wins\":15,\"pendingClaims\":3}]}");PlayerDataStore.Normalize(legacy);Check(legacy.version==5&&legacy.bossProgress[0].pendingClaims==3&&legacy.equipment.Count==0&&legacy.fishCoins==123&&legacy.tickets==7,"Legacy mastery preserved");
    }
}
