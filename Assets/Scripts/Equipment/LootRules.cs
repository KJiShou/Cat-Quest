using System;
using System.Linq;

[Serializable]
public sealed class LootDrop
{
    public string itemId;
    public int duplicateFish;
}

[Serializable]
public sealed class LootReceipt
{
    public string id, completedUtc;
    public int count;
    public LootDrop[] drops;
}

// Separate from the nine mastery rewards: rarity never grants mastery or set bonuses.
public static class LootRules
{
    static EquipmentDefinition Item(string icon,string name,int tier,EquipmentSlot slot) =>
        new EquipmentDefinition("chest-"+icon.ToLowerInvariant(),name,tier,slot,icon);
    public static readonly EquipmentDefinition[] Catalog = {
        Item("Armor_HelmGolden","Golden Helm",3,EquipmentSlot.Head),
        Item("Armor_HeavyArmor","Heavy Armour",3,EquipmentSlot.Body),
        Item("Armor_GauntletsGold","Golden Gauntlets",3,EquipmentSlot.Weapon),
        Item("Weapon_ShortSword","Short Sword",3,EquipmentSlot.Weapon),
        Item("Armor_MagicCrown","Magic Crown",3,EquipmentSlot.Head),
        Item("Weapon_MagicStaff_01","Magic Staff",3,EquipmentSlot.Weapon),
        Item("Armor_GauntletsSteel","Steel Gauntlets",2,EquipmentSlot.Weapon),
        Item("Armor_ChestArmor","Chest Armour",2,EquipmentSlot.Body),
        Item("Armor_HelmSilver","Silver Helm",2,EquipmentSlot.Head),
        Item("Weapon_Scimitar_02","Royal Scimitar",2,EquipmentSlot.Weapon),
        Item("Weapon_SpearGolden","Golden Spear",2,EquipmentSlot.Weapon),
        Item("Weapon_DualSwords","Dual Swords",2,EquipmentSlot.Weapon),
        Item("Armor_BoneArmor","Bone Armour",1,EquipmentSlot.Body),
        Item("Armor_HelmetGray","Grey Helmet",1,EquipmentSlot.Head),
        Item("Armor_SkullMask","Skull Mask",1,EquipmentSlot.Head),
        Item("Weapon_Axe_01","Axe",1,EquipmentSlot.Weapon),
        Item("Weapon_Scimitar_01","Scimitar",1,EquipmentSlot.Weapon),
        Item("Weapon_SpearSilver","Silver Spear",1,EquipmentSlot.Weapon)
    };
    public static string Rarity(int tier) => tier==3 ? "Legendary" : tier==2 ? "Ultra Rare" : "Rare";
    public static int Percent(int tier) => tier==3 ? 1 : tier==2 ? 10 : 89;
    public static int DuplicateFish(int tier) => tier==3 ? 300 : tier==2 ? 25 : 5;
    // Test rarity in order, then choose uniformly from that rarity's equipment pool.
    // The second test is conditional: 99/100 * 10/99 = 10% overall Ultra Rare.
    public static EquipmentDefinition Pick(Func<int,int> next)
    {
        if(next==null)throw new ArgumentNullException(nameof(next));
        int Roll(int exclusiveMax){
            int value=next(exclusiveMax);
            if(value<0||value>=exclusiveMax)throw new ArgumentOutOfRangeException(nameof(next));
            return value;
        }
        int tier;
        if(Roll(100)<Percent(3))tier=3;
        else if(Roll(100-Percent(3))<Percent(2))tier=2;
        else tier=1;
        var pool=Catalog.Where(d=>d.Tier==tier).ToArray();
        return pool[Roll(pool.Length)];
    }
}
