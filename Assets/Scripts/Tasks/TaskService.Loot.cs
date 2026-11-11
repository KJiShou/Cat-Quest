using System;
using System.Linq;

public sealed partial class TaskService
{
    readonly Random lootRandom = new Random();
    bool drawingLoot;
    public LootReceipt LastLootReceipt => store.Data.lootReceipts.Count==0 ? null : PlayerDataStore.Copy(store.Data.lootReceipts.Last());
    public string LootError(int count)
    {
        if(count!=1&&count!=10)return "Choose one chest or ten chests.";
        if(HasActiveBattle)return "Finish the current battle before opening a chest.";
        return Tickets<count ? "Not enough Boss Tickets. Earn more through Tasks or Focus." : null;
    }
    public bool DrawLoot(int count,string requestId,out LootReceipt receipt) => DrawLoot(count,requestId,out receipt,lootRandom.Next);
    // Internal injection is used only by isolated verification; UI cannot choose an item.
    internal bool DrawLoot(int count,string requestId,out LootReceipt receipt,Func<int,int> next)
    {
        receipt=null;
        if(string.IsNullOrWhiteSpace(requestId)||requestId.Length>128){LastError="Invalid chest request.";return false;}
        var saved=store.Data.lootReceipts.FirstOrDefault(r=>r.id==requestId);
        if(saved!=null){
            if(saved.count!=count){LastError="Chest request does not match.";return false;}
            receipt=PlayerDataStore.Copy(saved);LastError=null;return true;
        }
        if(drawingLoot){LastError="A chest is already being saved.";return false;}
        var error=LootError(count);if(error!=null){LastError=error;return false;}
        drawingLoot=true;
        try {
            var result=new LootReceipt{id=requestId,count=count,completedUtc=clock.UtcNow.ToString("o"),drops=new LootDrop[count]};
            bool success=Commit(data=>{
                data.tickets-=count;
                for(int i=0;i<count;i++){
                    var item=LootRules.Pick(next);
                    bool duplicate=data.equipment.Any(e=>e.id==item.Id);
                    int fish=duplicate?LootRules.DuplicateFish(item.Tier):0;
                    if(duplicate)data.fishCoins=checked(data.fishCoins+fish);
                    else data.equipment.Add(new EquipmentItem{id=item.Id,level=1});
                    result.drops[i]=new LootDrop{itemId=item.Id,duplicateFish=fish};
                }
                data.equipmentUnlocked=true;
                data.lootReceipts.Add(result);
            });
            if(success)receipt=PlayerDataStore.Copy(result);
            return success;
        } finally {drawingLoot=false;}
    }
}
