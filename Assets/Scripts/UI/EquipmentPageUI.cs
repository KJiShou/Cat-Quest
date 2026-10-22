using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EquipmentPageUI : MonoBehaviour
{
    public EquipmentItemDetailUI itemDetailPrefab;
    public EquipmentStatsUI statsPrefab;
    public EquipmentInventoryUI inventoryPrefab;
    public Sprite[] emptySlotIcons;
    EquipmentInventoryUI inventory;
    EquipmentItemDetailUI itemDetail;
    public bool claimsOnly; public Texture2D[] gradeSheets; public Sprite catPoster; public Sprite[] statIcons; public Sprite[] tabIcons; public Sprite tabFocus;
    public TMP_FontAsset font; public Sprite rounded,blueButton,redButton;public Sprite[] slotIcons;public GameObject catPrefab;public MainMenuNavigation navigation;
    TaskUIBuilder ui;TaskService service;TMP_Text title,notice,sets,modalTitle,modalInfo,confirmInfo;
    RectTransform backpack,modal,confirmation,choices,gear;Button[] slots=new Button[3];Image[] slotImages=new Image[3];
    Button[] filters=new Button[4]; int filter;
    string selected;int expectedLevel;Action confirmAction;
    public bool Ready=>service!=null;public string SelectedItem=>selected;
    static Color Pale=>new Color32(239,248,255,255);
    void Awake(){ui=new TaskUIBuilder(font,rounded);service=GameDataManager.EnsureInstance().Tasks;Build();}
    void OnEnable(){if(Ready){service.Changed+=Refresh;Refresh();}}
    void OnDisable(){if(Ready)service.Changed-=Refresh;if(itemDetail)itemDetail.gameObject.SetActive(false);if(modal)modal.gameObject.SetActive(false);if(confirmation)confirmation.gameObject.SetActive(false);}
    RectTransform Rect(Transform p,string n,float x0,float y0,float x1,float y1){var r=TaskUIBuilder.Rect(p,n);r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero;return r;}
    RectTransform Panel(Transform p,string n,float x0,float y0,float x1,float y1,Color c){var r=Rect(p,n,x0,y0,x1,y1);ui.Image(r,c,rounded).raycastTarget=false;return r;}
    TMP_Text Text(Transform p,string n,string value,float size,float x0,float y0,float x1,float y1,Color? c=null){var t=ui.Text(p,value,size,c);t.name=n;t.rectTransform.anchorMin=new Vector2(x0,y0);t.rectTransform.anchorMax=new Vector2(x1,y1);t.rectTransform.offsetMin=t.rectTransform.offsetMax=Vector2.zero;return t;}
    Button Button(Transform p,string n,string caption,float x0,float y0,float x1,float y1,Action click,bool red=false){var r=Panel(p,n,x0,y0,x1,y1,Color.white);var image=r.GetComponent<Image>();image.sprite=red?redButton:blueButton;image.pixelsPerUnitMultiplier=1;image.raycastTarget=true;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;b.onClick.AddListener(()=>click());Text(r,"Label",caption,21,.04f,.08f,.96f,.92f,Color.white).alignment=TextAlignmentOptions.Center;return b;}
    Image Icon(Transform p,EquipmentSlot slot,float x0,float y0,float x1,float y1){var image=ui.Image(Rect(p,"Icon",x0,y0,x1,y1),Color.white,slotIcons[(int)slot]);image.type=Image.Type.Simple;image.preserveAspect=true;image.raycastTarget=false;return image;}
    void Build(){
        if(!claimsOnly)BuildHome();
        modal=Overlay("ItemModal");var card=Panel(modal,"Card",.25f,.19f,.75f,.82f,Color.white);modalTitle=Text(card,"Title","",27,.06f,.82f,.84f,.96f,TaskUIBuilder.Blue);
        Button(card,"Close","x",.87f,.84f,.96f,.96f,()=>modal.gameObject.SetActive(false));modalInfo=Text(card,"Details","",22,.07f,.30f,.93f,.82f);
        choices=Rect(card,"Choices",.05f,.12f,.95f,.70f);
        confirmation=Overlay("ConfirmEquipment");var dialog=Panel(confirmation,"Card",.30f,.31f,.70f,.69f,Color.white);confirmInfo=Text(dialog,"Details","",23,.07f,.32f,.93f,.94f);confirmInfo.alignment=TextAlignmentOptions.Center;
        Button(dialog,"Confirm","Confirm",.06f,.08f,.48f,.25f,()=>confirmAction?.Invoke());Button(dialog,"Cancel","Cancel",.52f,.08f,.94f,.25f,()=>{confirmation.gameObject.SetActive(false);if(itemDetail&&itemDetail.gameObject.activeSelf)itemDetail.FocusActions();},true);
        itemDetail=Instantiate(itemDetailPrefab,transform);itemDetail.name="ItemDetail";
        itemDetail.close.onClick.AddListener(()=>itemDetail.gameObject.SetActive(false));
        itemDetail.equip.onClick.AddListener(()=>{if(service.Snapshot.equipment.Any(x=>x.id==selected))ToggleEquip();else ConfirmClaim();});
        itemDetail.upgrade.onClick.AddListener(Upgrade);
        itemDetail.gameObject.SetActive(false);
        modal.gameObject.SetActive(false);confirmation.gameObject.SetActive(false);
    }
    RectTransform Overlay(string n){var r=Rect(transform,n,0,0,1,1);ui.Image(r,new Color(0,.07f,.15f,.55f)).raycastTarget=true;var layer=r.gameObject.AddComponent<Canvas>();layer.overrideSorting=true;layer.sortingOrder=n=="ConfirmEquipment"?110:100;r.gameObject.AddComponent<EquipmentModalSurface>();return r;}
    static Color Grade(int tier)=>tier==1?new Color32(67,184,255,255):tier==2?new Color32(182,112,245,255):new Color32(255,201,65,255);
    void BuildHome(){
        var surface=(RectTransform)transform.Find("EquipmentSurface");
        var left=(RectTransform)surface.Find("CharacterCard");
        title=Text(left,"Title","Cat Home",27,.05f,.92f,.95f,.99f,TaskUIBuilder.Blue);
        var picture=ui.Image(Rect(left,"CatPreview",.04f,.50f,.96f,.91f),Color.white,catPoster);picture.type=Image.Type.Simple;picture.preserveAspect=true;picture.raycastTarget=false;
        var existingPreview=navigation?navigation.pages[0].GetComponentInChildren<CharacterHomePreview>(true):null;
        if(existingPreview&&existingPreview.previewCamera&&existingPreview.character){var live=Rect(left,"LiveCat",.04f,.50f,.96f,.91f);live.gameObject.AddComponent<RawImage>();var preview=live.gameObject.AddComponent<CharacterHomePreview>();preview.enabled=false;preview.previewCamera=existingPreview.previewCamera;preview.character=existingPreview.character;preview.showSceneBackground=true;preview.enabled=true;}
        gear=Rect(left,"EquippedGear",.16f,.34f,.84f,.49f);
        for(int i=0;i<3;i++){int index=i;slots[i]=Button(gear,"Slot"+i,"",0,0,1,1,()=>OpenSlot((EquipmentSlot)index));slots[i].GetComponent<Image>().sprite=rounded;slots[i].GetComponent<Image>().color=Pale;slotImages[i]=Icon(slots[i].transform,(EquipmentSlot)i,.29f,.29f,.71f,.71f);}
        sets=Text(left,"SetBonuses","",16,.04f,.285f,.96f,.335f,TaskUIBuilder.Blue);sets.alignment=TextAlignmentOptions.Center;
        var stats=Instantiate(statsPrefab,left);stats.name="Stats";
        var statsRect=(RectTransform)stats.transform;statsRect.anchorMin=new Vector2(.05f,.02f);statsRect.anchorMax=new Vector2(.95f,.275f);statsRect.offsetMin=statsRect.offsetMax=Vector2.zero;
        inventory=Instantiate(inventoryPrefab,surface);inventory.name="InventoryCard";
        backpack=inventory.backpack;notice=inventory.notice;filters=inventory.filters;
        for(int i=0;i<filters.Length;i++){int index=i;filters[i].onClick.AddListener(()=>{filter=index;Refresh();});}
        inventory.autoSelect.onClick.AddListener(AutoSelect);
    }
    void LateUpdate(){
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
    public void Refresh(){
        if(!Ready||claimsOnly)return;var d=service.Snapshot;var s=service.EquipmentStats;TaskService.CatLevel(d.experience,out int level,out _,out _);title.text=$"Cat Home  <size=20>Lv. {level}</size>";
        notice.text=service.EquipmentUnlocked?"":"Claim a Daily Reward to unlock equipment.";
        for(int i=0;i<3;i++){var id=EquipmentRules.Equipped(d,(EquipmentSlot)i);var item=d.equipment.FirstOrDefault(x=>x.id==id);slotImages[i].sprite=item==null?emptySlotIcons[i]:slotIcons[i];slotImages[i].color=item==null?new Color32(161,172,198,255):Color.white;slots[i].GetComponent<Image>().color=item==null?Pale:Color.Lerp(Grade(EquipmentRules.Find(id).Tier),Color.white,.75f);}
        sets.text=string.Join("  ",Enumerable.Range(1,3).Where(t=>s.SetPieces[t-1]>0).Select(t=>$"{EquipmentRules.BossName(t)} {s.SetPieces[t-1]}/3"));if(sets.text.Length==0)sets.text="No set equipped";
        for(int i=0;i<filters.Length;i++){inventory.filterIcons[i].color=filter==i?new Color32(88,121,255,255):new Color32(193,201,208,255);inventory.filterFocus[i].SetActive(filter==i);}
        foreach(Transform child in backpack){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        var items=d.equipment.Where(x=>filter==0||(filter==1&&EquipmentRules.Find(x.id).Slot==EquipmentSlot.Head)||(filter==2&&EquipmentRules.Find(x.id).Slot==EquipmentSlot.Weapon)||(filter==3&&EquipmentRules.Find(x.id).Slot==EquipmentSlot.Body)).OrderBy(x=>EquipmentRules.Find(x.id).Tier).ThenBy(x=>EquipmentRules.Find(x.id).Slot).ToArray();if(items.Length==0&&service.EquipmentUnlocked)notice.text=filter==0?"Earn equipment from Boss mastery.":"No equipment in this category.";
        notice.gameObject.SetActive(!string.IsNullOrEmpty(notice.text));
        foreach(var item in items){
            var def=EquipmentRules.Find(item.id);string id=item.id;
            var cell=Instantiate(inventory.itemTemplate,backpack);cell.name="Item_"+id;
            cell.background.sprite=inventory.tierBackgrounds[def.Tier-1];cell.icon.sprite=slotIcons[(int)def.Slot];
            cell.level.text=item.level.ToString();cell.equipped.SetActive(EquipmentRules.Equipped(d,def.Slot)==id);
            cell.button.onClick.AddListener(()=>OpenItem(id));cell.gameObject.SetActive(true);
        }
        for(int n=items.Length;n<24;n++){var cell=Instantiate(inventory.emptyTemplate,backpack);cell.name="EmptySlot";cell.SetActive(true);}
    }
    public void OpenSlot(EquipmentSlot slot){var id=EquipmentRules.Equipped(service.Snapshot,slot);if(!string.IsNullOrEmpty(id))OpenItem(id);else {notice.text="Select an owned "+EquipmentRules.SlotName(slot)+" in the Backpack.";notice.gameObject.SetActive(true);}}
    public void OpenClaim(int boss){
        if(!Ready)return;itemDetail.gameObject.SetActive(false);modal.gameObject.SetActive(true);modal.SetAsLastSibling();choices.gameObject.SetActive(true);modalInfo.text="";modalTitle.text=EquipmentRules.BossName(boss)+" Set";TaskUIBuilder.Clear(choices);var d=service.Snapshot;var p=d.bossProgress.Single(x=>x.bossId==boss);
        Text(choices,"Progress",p.pendingClaims>0?$"Choose an unowned component - {p.pendingClaims} available":$"Mastery {p.wins} wins - rewards at 5 / 10 / 15",20,.02f,.80f,.98f,1,TaskUIBuilder.Blue);
        for(int i=0;i<3;i++){var def=EquipmentRules.Catalog.Single(x=>x.Tier==boss&&(int)x.Slot==i);bool owned=d.equipment.Any(x=>x.id==def.Id);float x=.02f+i*.33f;var b=Button(choices,"Choice"+i,"",x,.22f,x+.30f,.70f,()=>OpenItem(def.Id));b.GetComponent<Image>().sprite=rounded;b.GetComponent<Image>().pixelsPerUnitMultiplier=3;b.GetComponent<Image>().color=Color.Lerp(Grade(boss),Color.white,.72f);Icon(b.transform,def.Slot,.22f,.22f,.78f,.78f);var caption=Text(choices,"Caption"+i,owned?"Owned":EquipmentRules.SlotName(def.Slot),18,x,.08f,x+.30f,.20f,TaskUIBuilder.Ink);caption.alignment=TextAlignmentOptions.Center;b.interactable=!owned;}
    }
    public void OpenItem(string id){
        var def=EquipmentRules.Find(id);if(def==null)return;
        selected=id;var d=service.Snapshot;var item=d.equipment.FirstOrDefault(x=>x.id==id);
        bool owned=item!=null;expectedLevel=item?.level??1;
        modal.gameObject.SetActive(false);itemDetail.gameObject.SetActive(true);itemDetail.transform.SetAsLastSibling();
        itemDetail.itemName.text=def.Name;itemDetail.tier.text=EquipmentRules.BossName(def.Tier).ToUpperInvariant()+" SET";
        itemDetail.level.text=expectedLevel.ToString();itemDetail.itemIcon.sprite=slotIcons[(int)def.Slot];
        itemDetail.currentLabel.text=def.Slot==EquipmentSlot.Weapon?"Attack":def.Slot==EquipmentSlot.Body?"Defence":"Speed";
        itemDetail.currentValue.text=EquipmentRules.Effect(def,expectedLevel);itemDetail.nextValue.text=EquipmentRules.Effect(def,expectedLevel+1);
        int statIndex=def.Slot==EquipmentSlot.Weapon?1:def.Slot==EquipmentSlot.Body?2:3;
        if(statIcons!=null&&statIcons.Length>statIndex){itemDetail.currentIcon.sprite=statIcons[statIndex];itemDetail.nextIcon.sprite=statIcons[statIndex];}
        int set=service.EquipmentStats.SetPieces[def.Tier-1];
        itemDetail.setInfo.text=$"{EquipmentRules.BossName(def.Tier)} set";
        itemDetail.ShowSet(def.Tier,set);
        itemDetail.equip.gameObject.SetActive(!owned||!claimsOnly);
        itemDetail.upgrade.gameObject.SetActive(owned&&!claimsOnly);itemDetail.upgradeCaption.SetActive(owned&&!claimsOnly);
        itemDetail.price.text=$"{EquipmentRules.UpgradeCost(expectedLevel)} Fish";
        if(owned){
            itemDetail.equipLabel.text=EquipmentRules.Equipped(d,def.Slot)==id?"Unequip":"Equip";
            itemDetail.equip.interactable=service.EquipmentUnlocked;
            string error=service.UpgradeEquipmentError(id,expectedLevel);
            itemDetail.upgrade.interactable=error==null;itemDetail.status.text=error??"";
        }else{
            itemDetail.equipLabel.text="Claim";
            itemDetail.equip.interactable=service.EquipmentUnlocked&&d.bossProgress.Single(x=>x.bossId==def.Tier).pendingClaims>0;
            itemDetail.status.text=!service.EquipmentUnlocked?"Claim a Daily Reward to unlock equipment.":itemDetail.equip.interactable?"Uses 1 mastery reward. Starts at Lv. 1.":"No mastery rewards available.";
        }
        itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().SetPreview($"+{EquipmentRules.Bonus(def,expectedLevel+1)-EquipmentRules.Bonus(def,expectedLevel):0.##} {(def.Slot==EquipmentSlot.Weapon?"ATK":def.Slot==EquipmentSlot.Body?"DEF":"ASPD")}");
        itemDetail.FocusActions();
    }

    void Ask(string text,Action action){itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().Dismiss();confirmInfo.text=text;confirmAction=action;confirmation.gameObject.SetActive(true);confirmation.SetAsLastSibling();if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(confirmation.Find("Card/Confirm").gameObject);}
    void Result(bool success){if(success){confirmation.gameObject.SetActive(false);Refresh();OpenItem(selected);if(claimsOnly)OpenClaim(EquipmentRules.Find(selected).Tier);}else confirmInfo.text=service.LastError+"\nTry again or cancel.";}
    void ToggleEquip(){
        var def=EquipmentRules.Find(selected);string id=selected;
        bool worn=EquipmentRules.Equipped(service.Snapshot,def.Slot)==id;
        bool success=worn?service.Unequip(def.Slot):service.Equip(id,def.Slot);
        if(success){Result(true);itemDetail.status.text=worn?"Unequipped.":"Equipped.";}
        else itemDetail.status.text=service.LastError;
    }
    void AutoSelect(){
        if(service.AutoEquip())Refresh();
        else {notice.text=service.LastError;notice.gameObject.SetActive(true);}
    }
    void Upgrade(){
        if(!itemDetail.upgrade.interactable)return;
        itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().Dismiss();
        bool success=service.UpgradeEquipment(selected,expectedLevel,Guid.NewGuid().ToString("N"));
        if(success){Result(true);itemDetail.status.text="Upgraded.";}
        else {string error=service.LastError;OpenItem(selected);itemDetail.status.text=error;}
    }
    void ConfirmClaim(){string id=selected;Ask("Claim "+EquipmentRules.Find(id).Name+"?\nUses 1 mastery reward. Starts at Lv. 1.",()=>Result(service.ClaimEquipment(id)));}
}
