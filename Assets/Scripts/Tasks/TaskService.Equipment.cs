using System;
using System.Linq;

public sealed partial class TaskService
{
    public bool EquipmentUnlocked=>store.Data.equipmentUnlocked||store.Data.rewardDays.Any(r=>r.questsClaimed||r.focusClaimed);
    public EquipmentStats EquipmentStats=>EquipmentRules.Calculate(store.Data);
    string EquipmentAccessError()=>!EquipmentUnlocked?"Claim your first Daily Reward to unlock equipment.":store.Data.battles.Any(b=>b.outcome==BattleOutcome.Running)?"Finish the current battle before changing equipment.":null;
    public bool ClaimEquipment(string itemId)
    {
        var error=EquipmentAccessError();if(error!=null){LastError=error;return false;}
        var def=EquipmentRules.Find(itemId);
        if(def==null){LastError="Unknown equipment.";return false;}
        // Repeating a saved claim does not consume another milestone.
        if(store.Data.equipment.Any(i=>i.id==itemId)){LastError="This component is already owned.";return false;}
        var progress=store.Data.bossProgress.Single(p=>p.bossId==def.Tier);
        if(progress.pendingClaims<=0){LastError="Win 5, 10 or 15 battles against this boss to choose equipment.";return false;}
        return Commit(d=>{d.equipmentUnlocked=true;d.equipment.Add(new EquipmentItem{id=itemId,level=1});var p=d.bossProgress.Single(x=>x.bossId==def.Tier);p.pendingClaims--;p.claimedComponents++;});
    }
    public bool Equip(string itemId,EquipmentSlot slot)
    {
        var error=EquipmentAccessError();if(error!=null){LastError=error;return false;}
        var def=EquipmentRules.Find(itemId);
        if(def==null||def.Slot!=slot||!store.Data.equipment.Any(i=>i.id==itemId)){LastError="This item is not owned or does not fit this slot.";return false;}
        if(EquipmentRules.Equipped(store.Data,slot)==itemId){LastError=null;return true;}
        return Commit(d=>{d.equipmentUnlocked=true;EquipmentRules.SetEquipped(d,slot,itemId);});
    }
    public bool AutoEquip()
    {
        var error=EquipmentAccessError();if(error!=null){LastError=error;return false;}
        var preview=Snapshot;
        var options=Enumerable.Range(0,3).Select(slot=>preview.equipment.Where(i=>(int)EquipmentRules.Find(i.id).Slot==slot).Select(i=>i.id).Prepend((string)null).ToArray()).ToArray();
        string[] best=Enumerable.Range(0,3).Select(i=>EquipmentRules.Equipped(preview,(EquipmentSlot)i)).ToArray();
        double power=EquipmentRules.Calculate(preview).Final.Power;
        foreach(var weapon in options[0])foreach(var body in options[1])foreach(var head in options[2]){
            EquipmentRules.SetEquipped(preview,EquipmentSlot.Weapon,weapon);EquipmentRules.SetEquipped(preview,EquipmentSlot.Body,body);EquipmentRules.SetEquipped(preview,EquipmentSlot.Head,head);
            double candidate=EquipmentRules.Calculate(preview).Final.Power;
            if(candidate>power){power=candidate;best=new[]{weapon,body,head};}
        }
        return Commit(d=>{for(int i=0;i<3;i++)EquipmentRules.SetEquipped(d,(EquipmentSlot)i,best[i]);});
    }
    public bool Unequip(EquipmentSlot slot)
    {
        var error=EquipmentAccessError();if(error!=null){LastError=error;return false;}
        if(!Enum.IsDefined(typeof(EquipmentSlot),slot)){LastError="Invalid equipment slot.";return false;}
        return Commit(d=>EquipmentRules.SetEquipped(d,slot,null));
    }
    public string UpgradeEquipmentError(string itemId,int expectedLevel)
    {
        var access=EquipmentAccessError();if(access!=null)return access;
        var item=store.Data.equipment.FirstOrDefault(i=>i.id==itemId);
        if(item==null||EquipmentRules.Find(itemId)==null)return "This item is not owned.";
        if(item.level!=expectedLevel)return "Equipment changed. Review the new upgrade cost.";
        CatLevel(store.Data.experience,out int level,out _,out _);
        if(item.level>=level)return "Reach Cat Lv. "+(item.level+1)+" to upgrade.";
        return store.Data.fishCoins<EquipmentRules.UpgradeCost(item.level)?"Not enough Fish Coins.":null;
    }
    public bool UpgradeEquipment(string itemId,int expectedLevel,string requestId)
    {
        if(string.IsNullOrWhiteSpace(requestId)){LastError="Missing upgrade request.";return false;}
        var receipt=store.Data.equipmentUpgrades.FirstOrDefault(r=>r.id==requestId);
        if(receipt!=null){bool matches=receipt.itemId==itemId&&receipt.fromLevel==expectedLevel;LastError=matches?null:"Upgrade request does not match.";return matches;}
        var error=UpgradeEquipmentError(itemId,expectedLevel);if(error!=null){LastError=error;return false;}
        return Commit(d=>{var item=d.equipment.Single(i=>i.id==itemId);d.fishCoins-=EquipmentRules.UpgradeCost(item.level);item.level++;d.equipmentUpgrades.Add(new EquipmentUpgradeReceipt{id=requestId,itemId=itemId,fromLevel=expectedLevel});d.equipmentUnlocked=true;});
    }
}
