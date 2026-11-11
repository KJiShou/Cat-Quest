using UnityEngine;

public sealed class LootArt : ScriptableObject
{
    public Sprite[] icons;
    public Vector2 frameCanvasSize=new Vector2(1200,1200);
    public int releaseFrame=28;
    public Sprite[] blueOpening, purpleOpening, goldOpening;
    public Sprite Icon(string id)
    {
        for(int i=0;i<LootRules.Catalog.Length;i++)
            if(LootRules.Catalog[i].Id==id&&i<icons.Length)return icons[i];
        return null;
    }
    public Sprite[] Opening(int tier) => tier==3?goldOpening:tier==2?purpleOpening:blueOpening;
}
