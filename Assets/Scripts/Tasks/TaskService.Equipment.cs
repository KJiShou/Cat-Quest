using System;
using System.Linq;

public sealed partial class TaskService
{
    public bool EquipmentUnlocked=>store.Data.equipmentUnlocked||store.Data.rewardDays.Any(r=>r.questsClaimed||r.focusClaimed);
    public string EquippedItem(EquipmentSlot slot) => EquipmentRules.Equipped(store.Data,slot);
    public bool OwnsEquipment(string id) => store.Data.equipment.Exists(item=>item.id==id);
    public EquipmentStats EquipmentStats=>EquipmentRules.Calculate(store.Data);
    // Detached equipment-only presentation/preview data. Never persist this projection
    // as a complete save: private tasks and historical receipts are intentionally absent.
    public PlayerData EquipmentSnapshot
    {
        get {
            var data=store.Data;
            return new PlayerData {
                experience=data.experience, fishCoins=data.fishCoins, equipmentUnlocked=EquipmentUnlocked,
                equippedWeaponId=data.equippedWeaponId, equippedBodyOutfitId=data.equippedBodyOutfitId,
                equippedHeadAccessoryId=data.equippedHeadAccessoryId,
                equipment=data.equipment.ConvertAll(item=>new EquipmentItem { id=item.id, level=item.level }),
                bossProgress=data.bossProgress.ConvertAll(progress=>new BossProgress { bossId=progress.bossId,
                    wins=progress.wins, pendingClaims=progress.pendingClaims, claimedComponents=progress.claimedComponents })
            };
        }
    }
    string EquipmentAccessError()=>!EquipmentUnlocked?"Claim your first Daily Reward to unlock equipment.":store.Data.battles.Any(b=>b.outcome==BattleOutcome.Running)?"Finish the current battle before changing equipment.":null;
    public string ClaimEquipmentError(string itemId)
    {
        var error=EquipmentAccessError();if(error!=null)return error;
        var def=EquipmentRules.Find(itemId);if(def==null)return "Unknown equipment.";
        if(def.IsLoot)return "Available from the equipment chest in Shop.";
        if(store.Data.equipment.Any(i=>i.id==itemId))return "This component is already owned.";
        var progress=store.Data.bossProgress.Single(p=>p.bossId==def.Tier);
        int required=5*((int)def.Slot+1);
        if(progress.wins<required)return $"Requires {required} wins against this boss.";
        return progress.pendingClaims<=0?"No mastery rewards available.":null;
    }
    public bool ClaimEquipment(string itemId)
    {
        var error=ClaimEquipmentError(itemId);if(error!=null){LastError=error;return false;}
        var def=EquipmentRules.Find(itemId);
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
        var preview=EquipmentSnapshot;
        var options=Enumerable.Range(0,3).Select(slot=>preview.equipment.Where(i=>(int)EquipmentRules.Find(i.id).Slot==slot).Select(i=>i.id).Prepend((string)null).ToArray()).ToArray();
        string[] best=Enumerable.Range(0,3).Select(i=>EquipmentRules.Equipped(preview,(EquipmentSlot)i)).ToArray();
        int stage=MasterStage>0&&BossUnlockError(1+(MasterStage-1)%3,MasterStage)==null?MasterStage:0;
        int bossId=stage>0?1+(stage-1)%3:BossUnlockError(3)==null?3:BossUnlockError(2)==null?2:1;
        var target=BattleRules.Boss(bossId,stage);
        double power=EquipmentRules.AutoEquipScore(preview,target);
        foreach(var weapon in options[0])foreach(var body in options[1])foreach(var head in options[2]){
            EquipmentRules.SetEquipped(preview,EquipmentSlot.Weapon,weapon);EquipmentRules.SetEquipped(preview,EquipmentSlot.Body,body);EquipmentRules.SetEquipped(preview,EquipmentSlot.Head,head);
            double candidate=EquipmentRules.AutoEquipScore(preview,target);
            if(candidate>power+1e-9){power=candidate;best=new[]{weapon,body,head};}
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
