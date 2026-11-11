using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LootVerification
{
    [Serializable] sealed class BrowserRecord { public int format=1; public string primary,backup,beforeShop; }
    sealed class Memory : IPlayerDataStore
    {
        public PlayerData Data {get;private set;}=new PlayerData();
        public bool fail;
        public bool TrySave(PlayerData data,out string error){error=fail?"Disk unavailable":null;if(fail)return false;PlayerDataStore.Normalize(data);Data=data;return true;}
    }
    static LootVerification(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-loot.request"))return;
        File.Delete("Temp/verify-loot.request");
        try{Run();File.WriteAllText("Temp/verify-loot.result",DateTime.UtcNow.ToString("O")+" PASS: sequential rarity tests and exact overall probabilities, duplicates, atomic batch, funds, retries, request identity, reload, migration backup, mastery isolation, boss balance; Equipment aggregate passed.");}
        catch(Exception e){File.WriteAllText("Temp/verify-loot.result",DateTime.UtcNow.ToString("O")+" FAIL: "+e);Debug.LogException(e);}
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static bool Draw(TaskService service,int count,string id,int tier,out LootReceipt receipt){
        object[] args={count,id,null,(Func<int,int>)(max=>max==100?(tier==3?0:1):max==99?(tier==2?0:10):0)};
        bool ok=(bool)typeof(TaskService).GetMethod("DrawLoot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(service,args);
        receipt=(LootReceipt)args[2];return ok;
    }
    static int Xp(int level){int xp=0;for(int i=1;i<level;i++)xp+=100+25*(i-1);return xp;}
    public static void Run(){
        EquipmentVerification.Run();
        Check(LootRules.Catalog.Length==18&&LootRules.Catalog.Select(x=>x.Id).Distinct().Count()==18,"18 unique items");
        // Enumerate independent RNG choices, including unused UR values after a Legendary success.
        var outcomes=new System.Collections.Generic.Dictionary<string,int>();
        foreach(var item in LootRules.Catalog)outcomes[item.Id]=0;
        for(int legendary=0;legendary<100;legendary++)for(int ultra=0;ultra<99;ultra++)for(int item=0;item<6;item++){
            var bounds=new System.Collections.Generic.List<int>();
            var picked=LootRules.Pick(max=>{bounds.Add(max);return max==100?legendary:max==99?ultra:item;});
            Check(bounds.SequenceEqual(legendary==0?new[]{100,6}:new[]{100,99,6}),"Skip later rarity test after success; select item last");
            Check(picked.Tier==(legendary==0?3:ultra<10?2:1),"Sequential rarity boundary");
            outcomes[picked.Id]++;
        }
        foreach(var item in LootRules.Catalog)Check(outcomes[item.Id]==LootRules.Percent(item.Tier)*99,"Exact overall rate and equal pool choice: "+item.Id);
        foreach(int stage in new[]{100,99,6})foreach(bool negative in new[]{false,true}){
            bool rejected=false;
            try{LootRules.Pick(max=>max==stage?(negative?-1:max):max==100?1:max==99?10:0);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"Reject invalid RNG value at every stage");
        }
        var mixedStore=new Memory();mixedStore.Data.tickets=10;var mixedService=new TaskService(mixedStore,new SystemTaskClock());
        var draws=new System.Collections.Generic.Queue<int>();
        for(int i=0;i<10;i++)foreach(int value in i%3==0?new[]{0,0}:i%3==1?new[]{1,0,0}:new[]{1,10,0})draws.Enqueue(value);
        var drawMethod=typeof(TaskService).GetMethod("DrawLoot",BindingFlags.Instance|BindingFlags.NonPublic);
        object[] mixedArgs={10,"mixed",null,(Func<int,int>)(max=>draws.Dequeue())};
        Check((bool)drawMethod.Invoke(mixedService,mixedArgs)&&draws.Count==0,"Ten pulls each run their own conditional tests");
        var mixedReceipt=(LootReceipt)mixedArgs[2];
        Check(mixedReceipt.drops.Select(d=>EquipmentRules.Find(d.itemId).Tier).SequenceEqual(Enumerable.Range(0,10).Select(i=>3-i%3)),"Mixed ten-pull rarity order");
        Check(mixedService.Tickets==0&&mixedService.FishCoins==960&&mixedStore.Data.equipment.Count==3,"Mixed batch settles once with correct duplicate values");
        object[] replayArgs={10,"mixed",null,(Func<int,int>)(max=>throw new Exception("Replay must not use RNG"))};
        Check((bool)drawMethod.Invoke(mixedService,replayArgs)&&mixedService.FishCoins==960,"Receipt replay skips every random test");
        var memory=new Memory();var service=new TaskService(memory,new SystemTaskClock());memory.Data.tickets=10;
        memory.fail=true;Check(!Draw(service,10,"batch",3,out _)&&service.Tickets==10&&memory.Data.equipment.Count==0&&memory.Data.lootReceipts.Count==0,"Failed batch rolls back everything");memory.fail=false;
        Check(Draw(service,10,"batch",3,out var receipt),"Retry ten draw");
        Check(receipt.drops.Length==10&&receipt.drops.Count(x=>x.duplicateFish==300)==9&&service.FishCoins==2700&&service.Tickets==0&&memory.Data.equipment.Count==1,"Within-batch duplicates and exact cost");
        Check(Draw(service,10,"batch",1,out _)&&service.FishCoins==2700,"Duplicate request does not reroll or repay");
        Check(!Draw(service,1,"batch",3,out _)&&!Draw(service,1,"empty",3,out _)&&!Draw(service,2,"invalid",3,out _),"Identity, funds and count checks");
        memory.Data.tickets=1;memory.Data.battles.Add(new BattleAttempt{id="active",outcome=BattleOutcome.Running});
        Check(!Draw(service,1,"battle",3,out _)&&service.Tickets==1,"Active battle blocks chest purchase");memory.Data.battles.Clear();
        receipt.drops[0].itemId="tampered";Check(service.LastLootReceipt.drops[0].itemId!="tampered","Detached result");
        memory.Data.tickets=10;memory.Data.fishCoins=int.MaxValue;
        Check(!Draw(service,1,"overflow",3,out _)&&service.Tickets==10,"Overflow is atomic");memory.Data.fishCoins=0;
        foreach(int tier in new[]{2,1}){Check(Draw(service,1,"new"+tier,tier,out _),"New rarity");Check(Draw(service,1,"duplicate"+tier,tier,out var r)&&r.drops[0].duplicateFish==LootRules.DuplicateFish(tier),"Rarity duplicate conversion");}
        Check(!service.ClaimEquipment(LootRules.Catalog[1].Id)&&memory.Data.bossProgress.All(p=>p.claimedComponents==0&&p.pendingClaims==0),"Loot cannot consume or count as mastery");
        memory.Data.equipment=LootRules.Catalog.Select(d=>new EquipmentItem{id=d.Id}).ToList();memory.Data.experience=Xp(10);PlayerDataStore.Normalize(memory.Data);
        foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))Check(service.Equip(LootRules.Catalog.First(d=>d.Slot==slot&&d.Tier==3).Id,slot),"Loot equips");
        Check(service.EquipmentStats.SetPieces.All(n=>n==0)&&service.EquipmentStats.SetHp==0,"No premature series abilities");
        string folder="Library/LootVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);
        string path=Path.GetFullPath(folder+"/save.json");
        var legacy=PlayerDataStore.Copy(memory.Data);legacy.version=5;string original=JsonUtility.ToJson(legacy);File.WriteAllText(path,original);
        var disk=new PlayerDataStore(path);Check(disk.TrySave(disk.Data,out _),"Save migrated data");Check(File.ReadAllText(path+".before-shop.bak")==original,"Exact pre-shop backup");
        var reloaded=new TaskService(new PlayerDataStore(path),new SystemTaskClock());int balance=reloaded.FishCoins;
        Check(Draw(reloaded,10,"batch",1,out _)&&reloaded.FishCoins==balance&&reloaded.Snapshot.equipment.Count==18,"Reload retains loot and receipts");
        string browserJson=JsonUtility.ToJson(new BrowserRecord{primary=original,backup=original});string before=browserJson;bool browserFail=true;
        var browser=new BrowserPlayerSave(()=>browserJson,(expected,next)=>{
            if(browserFail)return "Browser write failed";
            if(expected!=browserJson)return "Stale browser write";
            browserJson=next;return null;
        });
        var browserDisk=new PlayerDataStore(Path.GetFullPath(folder+"/browser.json"),browser);
        Check(!browserDisk.TrySave(browserDisk.Data,out _)&&browserJson==before,"Browser migration failure is atomic");browserFail=false;
        Check(browserDisk.TrySave(browserDisk.Data,out _)&&JsonUtility.FromJson<BrowserRecord>(browserJson).beforeShop==original,"Browser preserves original pre-shop data");
        Check(browserDisk.TrySave(browserDisk.Data,out _)&&JsonUtility.FromJson<BrowserRecord>(browserJson).beforeShop==original,"Browser keeps migration backup on later writes");
        var report=new System.Text.StringBuilder();
        foreach(int boss in new[]{1,2,3}){
            int gate=boss==1?5:boss==2?10:20;
            var sim=new BattleSimulation(BattleRules.Cat(Xp(gate)),BattleRules.Boss(boss));sim.Advance(120);Check(sim.Outcome==BattleOutcome.Defeat,"Bare unlock level loses boss "+boss);
            foreach(int tier in new[]{0,1,2,3}){
                int first=0;
                for(int level=gate;level<=60;level++){
                    var d=new PlayerData{experience=Xp(level)};
                    if(tier>0)foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot))){var def=LootRules.Catalog.First(x=>x.Slot==slot&&x.Tier==tier);d.equipment.Add(new EquipmentItem{id=def.Id,level=level});EquipmentRules.SetEquipped(d,slot,def.Id);}
                    var battle=new BattleSimulation(EquipmentRules.Calculate(d).Final,BattleRules.Boss(boss));battle.Advance(120);
                    Check(battle.Outcome!=BattleOutcome.Error,"Balance must finish without timeout");
                    if(battle.Outcome==BattleOutcome.Victory){first=level;break;}
                }
                Check(first>0,"Boss remains beatable");report.AppendLine($"Boss {boss}, rarity {tier}, gear at Cat Level: first victory Lv.{first}");
            }
        }
        File.WriteAllText(folder+"/balance.txt",report.ToString());
    }
}
