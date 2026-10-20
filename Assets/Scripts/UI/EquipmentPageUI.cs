using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EquipmentPageUI : MonoBehaviour
{
    public bool claimsOnly; public Texture2D[] gradeSheets; public Sprite catPoster; public Sprite[] statIcons; public Sprite[] tabIcons; public Sprite tabFocus;
    public TMP_FontAsset font; public Sprite rounded,blueButton,redButton;public Sprite[] slotIcons;public GameObject catPrefab;public MainMenuNavigation navigation;
    TaskUIBuilder ui;TaskService service;TMP_Text title,notice,sets,empty,modalTitle,modalInfo,confirmInfo;
    RectTransform backpack,modal,confirmation,choices,gear;Button equip,upgrade,claim;Button[] slots=new Button[3];Image[] slotImages=new Image[3];
    TMP_Text[] statValues=new TMP_Text[4]; Image[] statFills=new Image[4]; Button[] filters=new Button[4]; GridLayoutGroup grid; int filter;
    string selected;int expectedLevel;Action confirmAction;
    public bool Ready=>service!=null;public string SelectedItem=>selected;
    static Color Pale=>new Color32(239,248,255,255);
    void Awake(){ui=new TaskUIBuilder(font,rounded);service=GameDataManager.EnsureInstance().Tasks;Build();}
    void OnEnable(){if(Ready){service.Changed+=Refresh;Refresh();}}
    void OnDisable(){if(Ready)service.Changed-=Refresh;if(modal)modal.gameObject.SetActive(false);if(confirmation)confirmation.gameObject.SetActive(false);}
    RectTransform Rect(Transform p,string n,float x0,float y0,float x1,float y1){var r=TaskUIBuilder.Rect(p,n);r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero;return r;}
    RectTransform Panel(Transform p,string n,float x0,float y0,float x1,float y1,Color c){var r=Rect(p,n,x0,y0,x1,y1);ui.Image(r,c,rounded).raycastTarget=false;return r;}
    TMP_Text Text(Transform p,string n,string value,float size,float x0,float y0,float x1,float y1,Color? c=null){var t=ui.Text(p,value,size,c);t.name=n;t.rectTransform.anchorMin=new Vector2(x0,y0);t.rectTransform.anchorMax=new Vector2(x1,y1);t.rectTransform.offsetMin=t.rectTransform.offsetMax=Vector2.zero;return t;}
    Button Button(Transform p,string n,string caption,float x0,float y0,float x1,float y1,Action click,bool red=false){var r=Panel(p,n,x0,y0,x1,y1,Color.white);var image=r.GetComponent<Image>();image.sprite=red?redButton:blueButton;image.pixelsPerUnitMultiplier=1;image.raycastTarget=true;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;b.onClick.AddListener(()=>click());Text(r,"Label",caption,21,.04f,.08f,.96f,.92f,Color.white).alignment=TextAlignmentOptions.Center;return b;}
    Image Icon(Transform p,EquipmentSlot slot,float x0,float y0,float x1,float y1){var image=ui.Image(Rect(p,"Icon",x0,y0,x1,y1),Color.white,slotIcons[(int)slot]);image.type=Image.Type.Simple;image.preserveAspect=true;image.raycastTarget=false;return image;}
    void Build(){
        if(!claimsOnly)BuildHome();
        modal=Overlay("ItemModal");var card=Panel(modal,"Card",.25f,.19f,.75f,.82f,Color.white);modalTitle=Text(card,"Title","",27,.06f,.82f,.84f,.96f,TaskUIBuilder.Blue);
        Button(card,"Close","x",.87f,.84f,.96f,.96f,()=>modal.gameObject.SetActive(false));modalInfo=Text(card,"Details","",22,.07f,.30f,.93f,.82f);
        equip=Button(card,"Equip","Equip",.06f,.09f,.47f,.23f,ConfirmEquip);upgrade=Button(card,"Upgrade","Upgrade",.53f,.09f,.94f,.23f,ConfirmUpgrade);claim=Button(card,"Claim","Claim",.25f,.09f,.75f,.23f,ConfirmClaim);choices=Rect(card,"Choices",.05f,.12f,.95f,.70f);
        confirmation=Overlay("ConfirmEquipment");var dialog=Panel(confirmation,"Card",.30f,.31f,.70f,.69f,Color.white);confirmInfo=Text(dialog,"Details","",23,.07f,.32f,.93f,.94f);confirmInfo.alignment=TextAlignmentOptions.Center;
        Button(dialog,"Confirm","Confirm",.06f,.08f,.48f,.25f,()=>confirmAction?.Invoke());Button(dialog,"Cancel","Cancel",.52f,.08f,.94f,.25f,()=>confirmation.gameObject.SetActive(false),true);
        modal.gameObject.SetActive(false);confirmation.gameObject.SetActive(false);
    }
    RectTransform Overlay(string n){var r=Rect(transform,n,0,0,1,1);ui.Image(r,new Color(0,.07f,.15f,.55f)).raycastTarget=true;return r;}
    static Color Grade(int tier)=>tier==1?new Color32(67,184,255,255):tier==2?new Color32(182,112,245,255):new Color32(255,201,65,255);
    void BuildHome(){
        var surface=Rect(transform,"EquipmentSurface",.012f,.02f,.99f,.98f);
        var left=Panel(surface,"CharacterCard",0,0,.395f,1,Color.white);
        title=Text(left,"Title","Cat Home",27,.05f,.92f,.95f,.99f,TaskUIBuilder.Blue);
        var picture=ui.Image(Rect(left,"CatPreview",.04f,.50f,.96f,.91f),Color.white,catPoster);picture.type=Image.Type.Simple;picture.preserveAspect=true;picture.raycastTarget=false;
        var existingPreview=navigation?navigation.pages[0].GetComponentInChildren<CharacterHomePreview>(true):null;
        if(existingPreview&&existingPreview.previewCamera&&existingPreview.character){var live=Rect(left,"LiveCat",.04f,.50f,.96f,.91f);live.gameObject.AddComponent<RawImage>();var preview=live.gameObject.AddComponent<CharacterHomePreview>();preview.enabled=false;preview.previewCamera=existingPreview.previewCamera;preview.character=existingPreview.character;preview.showSceneBackground=true;preview.enabled=true;}
        gear=Rect(left,"EquippedGear",.16f,.34f,.84f,.49f);
        for(int i=0;i<3;i++){int index=i;slots[i]=Button(gear,"Slot"+i,"",0,0,1,1,()=>OpenSlot((EquipmentSlot)index));slots[i].GetComponent<Image>().sprite=rounded;slots[i].GetComponent<Image>().color=Pale;slotImages[i]=Icon(slots[i].transform,(EquipmentSlot)i,.29f,.29f,.71f,.71f);}
        sets=Text(left,"SetBonuses","",16,.04f,.285f,.96f,.335f,TaskUIBuilder.Blue);sets.alignment=TextAlignmentOptions.Center;
        string[] labels={"Health","Attack","Defence","Speed"}; Color[] colors={new Color32(37,210,154,255),new Color32(255,96,121,255),new Color32(66,166,241,255),new Color32(182,103,237,255)};
        for(int i=0;i<4;i++){float y=.265f-i*.06f;var row=Rect(left,"Stat"+i,.05f,y-.045f,.95f,y);if(statIcons!=null&&i<statIcons.Length){var icon=ui.Image(Rect(row,"Icon",0,0,.065f,1),colors[i],statIcons[i]);icon.preserveAspect=true;icon.type=Image.Type.Simple;}Text(row,"Label",labels[i],17,.09f,0,.35f,1,colors[i]);var track=Panel(row,"Track",.36f,0,1,1,new Color32(222,228,242,255));statFills[i]=ui.Image(Rect(track,"Fill",0,0,.65f,1),colors[i],rounded);statFills[i].raycastTarget=false;statValues[i]=Text(track,"Value","",17,0,0,1,1,Color.white);statValues[i].alignment=TextAlignmentOptions.Center;statValues[i].fontStyle=FontStyles.Bold;}
        var right=Panel(surface,"InventoryCard",.415f,0,1,1,Color.white);
        Text(right,"Title","Equipment",27,.045f,.92f,.65f,.99f,TaskUIBuilder.Blue);
        notice=Text(right,"Notice","",17,.045f,.83f,.96f,.88f,TaskUIBuilder.Blue);
        for(int i=0;i<4;i++){
            int index=i;float x=.04f+i*.24f;
            var tab=Rect(right,"Filter"+i,x,.865f,x+.24f,.935f);
            var hit=tab.gameObject.AddComponent<Image>();hit.color=Color.clear;
            var button=tab.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;
            button.onClick.AddListener(()=>{filter=index;Refresh();});filters[i]=button;
            var icon=ui.Image(Rect(tab,"TabIcon",.32f,.23f,.68f,.85f),Color.white,tabIcons[i]);icon.type=Image.Type.Simple;icon.preserveAspect=true;icon.raycastTarget=false;
            var focus=ui.Image(Rect(tab,"TabFocus",.24f,0,.76f,.08f),new Color32(88,121,255,255),tabFocus);focus.raycastTarget=false;
            if(i<3){var line=ui.Image(Rect(tab,"Separator",.995f,.32f,1,.68f),new Color32(232,237,246,255));line.raycastTarget=false;}
        }

        backpack=Rect(surface,"Backpack",.44f,.15f,.975f,.80f);grid=backpack.gameObject.AddComponent<GridLayoutGroup>();grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=5;grid.spacing=new Vector2(14,16);grid.childAlignment=TextAnchor.UpperLeft;
        empty=Text(right,"Empty","Earn equipment from Boss mastery.",21,.08f,.3f,.92f,.7f,TaskUIBuilder.Blue);empty.alignment=TextAlignmentOptions.Center;
        Button(right,"AutoSelect","Auto Select",.27f,.035f,.73f,.115f,()=>Ask("Equip the strongest owned loadout?",()=>{if(service.AutoEquip()){confirmation.gameObject.SetActive(false);Refresh();}else confirmInfo.text=service.LastError;}));
    }
    void LateUpdate(){
        if(grid){float cell=Mathf.Max(0,Mathf.Min((backpack.rect.width-56)/5,(backpack.rect.height-32)/3));grid.cellSize=new Vector2(cell,cell);}
        if(gear){float side=Mathf.Max(0,Mathf.Min((gear.rect.width-32)/3,gear.rect.height));for(int i=0;i<3;i++)Square((RectTransform)slots[i].transform,new Vector2(.5f,.5f),side,new Vector2((i-1)*(side+16),0));}
        if(choices&&choices.gameObject.activeInHierarchy){float side=Mathf.Max(0,Mathf.Min(InventoryCellSize(),Mathf.Min(choices.rect.width*.30f,choices.rect.height*.64f)));for(int i=0;i<3;i++){var choice=choices.Find("Choice"+i) as RectTransform;if(choice)Square(choice,new Vector2(.17f+i*.33f,.46f),side,Vector2.zero);}}
    }
    float InventoryCellSize(){
        // Match the actual backpack grid even while the Cat Home page is inactive.
        var page=navigation?(RectTransform)navigation.pages[(int)MainMenuPage.CatHome].transform:(RectTransform)transform;
        float width=page.rect.width*(.99f-.012f)*(.975f-.44f);
        float height=page.rect.height*(.98f-.02f)*(.80f-.15f);
        return Mathf.Max(0,Mathf.Min((width-56)/5,(height-32)/3));
    }
    static void Square(RectTransform rect,Vector2 center,float side,Vector2 offset){
        rect.anchorMin=rect.anchorMax=center;rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(side,side);rect.anchoredPosition=offset;
    }
    void Effect(Transform parent,int tier){if(gradeSheets==null||gradeSheets.Length<tier)return;var r=Rect(parent,"GradeEffect",-.06f,-.06f,1.06f,1.06f);var raw=r.gameObject.AddComponent<RawImage>();raw.texture=gradeSheets[tier-1];raw.raycastTarget=false;raw.color=new Color(1,1,1,.8f);r.gameObject.AddComponent<EquipmentGradeEffect>();r.SetAsFirstSibling();}
    public void Refresh(){
        if(!Ready||claimsOnly)return;var d=service.Snapshot;var s=service.EquipmentStats;var c=s.Final;TaskService.CatLevel(d.experience,out int level,out _,out _);title.text=$"Cat Home  <size=20>Lv. {level}</size>";
        notice.text=service.EquipmentUnlocked?"":"Claim a Daily Reward to unlock equipment.";
        for(int i=0;i<3;i++){var id=EquipmentRules.Equipped(d,(EquipmentSlot)i);var item=d.equipment.FirstOrDefault(x=>x.id==id);slotImages[i].color=item==null?new Color(1,1,1,.25f):Color.white;slots[i].GetComponent<Image>().color=item==null?Pale:Color.Lerp(Grade(EquipmentRules.Find(id).Tier),Color.white,.75f);var old=slots[i].transform.Find("GradeEffect");if(old){old.gameObject.SetActive(false);Destroy(old.gameObject);}if(item!=null)Effect(slots[i].transform,EquipmentRules.Find(id).Tier);}
        double[] values={c.hp,c.atk,c.def,c.aspd};double[] scales={s.Base.hp*1.2,s.Base.atk+12,s.Base.def+12,2};
        for(int i=0;i<4;i++){statValues[i].text=i==3?$"{values[i]:0.00}/s":$"{values[i]:0.##}";statFills[i].rectTransform.anchorMax=new Vector2(Mathf.Clamp01((float)(values[i]/scales[i])),1);}
        sets.text=string.Join("  ",Enumerable.Range(1,3).Where(t=>s.SetPieces[t-1]>0).Select(t=>$"{EquipmentRules.BossName(t)} {s.SetPieces[t-1]}/3"));if(sets.text.Length==0)sets.text="No set equipped";
        for(int i=0;i<filters.Length;i++){filters[i].transform.Find("TabIcon").GetComponent<Image>().color=filter==i?new Color32(88,121,255,255):new Color32(193,201,208,255);filters[i].transform.Find("TabFocus").gameObject.SetActive(filter==i);}
        foreach(Transform child in backpack){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        var items=d.equipment.Where(x=>filter==0||(filter==1&&EquipmentRules.Find(x.id).Slot==EquipmentSlot.Head)||(filter==2&&EquipmentRules.Find(x.id).Slot==EquipmentSlot.Weapon)||(filter==3&&EquipmentRules.Find(x.id).Slot==EquipmentSlot.Body)).OrderBy(x=>EquipmentRules.Find(x.id).Tier).ThenBy(x=>EquipmentRules.Find(x.id).Slot).ToArray();empty.gameObject.SetActive(false);if(items.Length==0&&service.EquipmentUnlocked)notice.text=filter==0?"Earn equipment from Boss mastery.":"No equipment in this category.";
        foreach(var item in items){var def=EquipmentRules.Find(item.id);string id=item.id;var b=Button(backpack,"Item_"+id,"",0,0,1,1,()=>OpenItem(id));b.GetComponent<Image>().sprite=rounded;b.GetComponent<Image>().pixelsPerUnitMultiplier=3;b.GetComponent<Image>().color=Color.Lerp(Grade(def.Tier),Color.white,.72f);Effect(b.transform,def.Tier);Icon(b.transform,def.Slot,.22f,.22f,.78f,.78f);var label=b.GetComponentInChildren<TMP_Text>();label.text=$"Lv. {item.level}";label.color=TaskUIBuilder.Ink;label.fontSize=16;label.rectTransform.anchorMin=new Vector2(.12f,.025f);label.rectTransform.anchorMax=new Vector2(.88f,.25f);if(EquipmentRules.Equipped(d,def.Slot)==id){var mark=Text(b.transform,"Equipped","E",14,.72f,.75f,.95f,.97f,TaskUIBuilder.Blue);mark.alignment=TextAlignmentOptions.Center;}}
        for(int n=items.Length;n<15;n++){var cell=Panel(backpack,"EmptySlot",0,0,1,1,new Color32(246,248,253,255));Text(cell,"Plus","+",25,0,0,1,1,new Color32(211,221,235,255)).alignment=TextAlignmentOptions.Center;}
    }
    public void OpenSlot(EquipmentSlot slot){var id=EquipmentRules.Equipped(service.Snapshot,slot);if(!string.IsNullOrEmpty(id))OpenItem(id);else notice.text="Select an owned "+EquipmentRules.SlotName(slot)+" in the Backpack.";}
    public void OpenClaim(int boss){
        if(!Ready)return;modal.gameObject.SetActive(true);modal.SetAsLastSibling();choices.gameObject.SetActive(true);modalInfo.text="";modalTitle.text=EquipmentRules.BossName(boss)+" Set";equip.gameObject.SetActive(false);upgrade.gameObject.SetActive(false);claim.gameObject.SetActive(false);TaskUIBuilder.Clear(choices);var d=service.Snapshot;var p=d.bossProgress.Single(x=>x.bossId==boss);
        Text(choices,"Progress",p.pendingClaims>0?$"Choose an unowned component - {p.pendingClaims} available":$"Mastery {p.wins} wins - rewards at 5 / 10 / 15",20,.02f,.80f,.98f,1,TaskUIBuilder.Blue);
        for(int i=0;i<3;i++){var def=EquipmentRules.Catalog.Single(x=>x.Tier==boss&&(int)x.Slot==i);bool owned=d.equipment.Any(x=>x.id==def.Id);float x=.02f+i*.33f;var b=Button(choices,"Choice"+i,"",x,.22f,x+.30f,.70f,()=>OpenItem(def.Id));b.GetComponent<Image>().sprite=rounded;b.GetComponent<Image>().pixelsPerUnitMultiplier=3;b.GetComponent<Image>().color=Color.Lerp(Grade(boss),Color.white,.72f);Effect(b.transform,boss);Icon(b.transform,def.Slot,.22f,.22f,.78f,.78f);var caption=Text(choices,"Caption"+i,owned?"Owned":EquipmentRules.SlotName(def.Slot),18,x,.08f,x+.30f,.20f,TaskUIBuilder.Ink);caption.alignment=TextAlignmentOptions.Center;b.interactable=!owned;}
    }
    public void OpenItem(string id){
        selected=id;var def=EquipmentRules.Find(id);if(def==null)return;var d=service.Snapshot;var item=d.equipment.FirstOrDefault(x=>x.id==id);bool owned=item!=null;modal.gameObject.SetActive(true);choices.gameObject.SetActive(false);modalTitle.text=def.Name;expectedLevel=item?.level??1;var set=service.EquipmentStats.SetPieces[def.Tier-1];
        modalInfo.text=$"{EquipmentRules.SlotName(def.Slot)} - Equipment Lv. {expectedLevel}\n{EquipmentRules.Effect(def,expectedLevel)}\n\n{EquipmentRules.BossName(def.Tier)} set - {set}/3 equipped\n2 pieces: +{def.Tier*5}% HP\n3 pieces: +{def.Tier} ATK, +{def.Tier} DEF, +{def.Tier*.02:0.00} ASPD";
        equip.gameObject.SetActive(owned&&!claimsOnly);upgrade.gameObject.SetActive(owned&&!claimsOnly);claim.gameObject.SetActive(!owned);
        if(owned){bool worn=EquipmentRules.Equipped(d,def.Slot)==id;equip.GetComponentInChildren<TMP_Text>().text=worn?"Unequip":"Equip";equip.interactable=service.EquipmentUnlocked;upgrade.GetComponentInChildren<TMP_Text>().text=$"Upgrade - {EquipmentRules.UpgradeCost(expectedLevel)} Fish";upgrade.interactable=service.UpgradeEquipmentError(id,expectedLevel)==null;modalInfo.text+=$"\n\nNext: {EquipmentRules.Effect(def,expectedLevel+1)}";if(!upgrade.interactable)modalInfo.text+="\n<size=18>"+service.UpgradeEquipmentError(id,expectedLevel)+"</size>";}
        else{claim.interactable=service.EquipmentUnlocked&&d.bossProgress.Single(x=>x.bossId==def.Tier).pendingClaims>0;if(!service.EquipmentUnlocked)modalInfo.text+="\n<size=18>Claim a Daily Reward to unlock equipment.</size>";}
    }
    void Ask(string text,Action action){confirmInfo.text=text;confirmAction=action;confirmation.gameObject.SetActive(true);confirmation.SetAsLastSibling();}
    void Result(bool success){if(success){confirmation.gameObject.SetActive(false);Refresh();OpenItem(selected);if(notice)notice.text="Equipment saved.";if(claimsOnly)OpenClaim(EquipmentRules.Find(selected).Tier);}else confirmInfo.text=service.LastError+"\nTry again or cancel.";}
    void ConfirmEquip(){var def=EquipmentRules.Find(selected);string id=selected;bool worn=EquipmentRules.Equipped(service.Snapshot,def.Slot)==id;var preview=service.Snapshot;EquipmentRules.SetEquipped(preview,def.Slot,worn?null:id);var c=EquipmentRules.Calculate(preview).Final;Ask($"{(worn?"Unequip":"Equip")} {def.Name}?\nPower {service.CatStats.Power} -> {c.Power}\nHP {c.hp} - ATK {c.atk:0.##} - DEF {c.def:0.##}\nASPD {c.aspd:0.00}/s",()=>Result(worn?service.Unequip(def.Slot):service.Equip(id,def.Slot)));}
    void ConfirmUpgrade(){string id=selected;int level=expectedLevel;string token=Guid.NewGuid().ToString("N");var def=EquipmentRules.Find(id);Ask($"Upgrade {def.Name} to Lv. {level+1}?\n{EquipmentRules.Effect(def,level)} -> {EquipmentRules.Effect(def,level+1)}\nCost: {EquipmentRules.UpgradeCost(level)} Fish Coins",()=>Result(service.UpgradeEquipment(id,level,token)));}
    void ConfirmClaim(){string id=selected;Ask("Claim "+EquipmentRules.Find(id).Name+"?\nUses 1 mastery reward. Starts at Lv. 1.",()=>Result(service.ClaimEquipment(id)));}
}
