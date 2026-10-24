using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class EquipmentPageUI : MonoBehaviour
{
    public EquipmentItemDetailUI itemDetailPrefab;
    public EquipmentStatsUI statsPrefab;
    public EquipmentInventoryUI inventoryPrefab;
    public EquipmentClaimUI claimView;
    public Sprite[] emptySlotIcons;
    [SerializeField] EquipmentInventoryUI inventory;
    [SerializeField] EquipmentItemDetailUI itemDetail;
    public bool claimsOnly; public Texture2D[] gradeSheets; public Sprite catPoster; public Sprite[] statIcons; public Sprite[] tabIcons; public Sprite tabFocus;
    public TMP_FontAsset font; public Sprite rounded,blueButton,redButton;public Sprite[] slotIcons;public GameObject catPrefab;public MainMenuNavigation navigation;
    TaskService service;[SerializeField] TMP_Text title,notice,sets,modalTitle,modalInfo,confirmInfo;
    [SerializeField] RectTransform backpack,modal,confirmation,choices,gear;[SerializeField] Button[] slots=new Button[3];[SerializeField] Image[] slotImages=new Image[3];
    [SerializeField] Button[] filters=new Button[4]; int filter;
    EquipmentHoverTarget hoverSource;
    string selected;int expectedLevel;Action confirmAction;
    public bool Ready=>service!=null;public string SelectedItem=>selected;
    static Color Pale=>new Color32(239,248,255,255);
    void Awake()
    {
        if(!Application.IsPlaying(gameObject))return;
        service=GameDataManager.EnsureInstance().Tasks;
        BindControls();
    }
    void BindControls()
    {
        for(int i=0;i<claimView.buttons.Length;i++){int slot=i;claimView.buttons[i].onClick.AddListener(()=>OpenItem(EquipmentRules.Catalog.Single(x=>x.Tier==claimView.Boss&&(int)x.Slot==slot).Id));}
        modal.Find("Card/Close").GetComponent<Button>().onClick.AddListener(()=>modal.gameObject.SetActive(false));
        confirmation.Find("Card/Confirm").GetComponent<Button>().onClick.AddListener(()=>confirmAction?.Invoke());
        confirmation.Find("Card/Cancel").GetComponent<Button>().onClick.AddListener(()=>{confirmation.gameObject.SetActive(false);if(itemDetail&&itemDetail.gameObject.activeSelf)itemDetail.FocusActions();});
        itemDetail.close.onClick.AddListener(()=>itemDetail.gameObject.SetActive(false));
        itemDetail.equip.onClick.AddListener(()=>{if(service.Snapshot.equipment.Any(x=>x.id==selected))ToggleEquip();else ConfirmClaim();});
        itemDetail.upgrade.onClick.AddListener(Upgrade);
        itemDetail.gameObject.SetActive(false);modal.gameObject.SetActive(false);confirmation.gameObject.SetActive(false);
        if(claimsOnly)return;
        for(int i=0;i<slots.Length;i++){int index=i;slots[i].onClick.AddListener(()=>OpenSlot((EquipmentSlot)index));}
        for(int i=0;i<filters.Length;i++){int index=i;filters[i].onClick.AddListener(()=>{filter=index;Refresh();});}
        inventory.autoSelect.onClick.AddListener(AutoSelect);
    }
    void OnEnable(){if(Ready){service.Changed+=Refresh;Refresh();}}
    void OnDisable(){if(!Application.IsPlaying(gameObject))return;HideHover();if(Ready)service.Changed-=Refresh;if(itemDetail)itemDetail.gameObject.SetActive(false);if(modal)modal.gameObject.SetActive(false);if(confirmation)confirmation.gameObject.SetActive(false);}
    static Color Grade(int tier)=>tier==1?new Color32(67,184,255,255):tier==2?new Color32(182,112,245,255):new Color32(255,201,65,255);
    void LateUpdate(){
        if(gear){float side=Mathf.Max(0,Mathf.Min((gear.rect.width-32)/3,gear.rect.height));for(int i=0;i<3;i++)Square((RectTransform)slots[i].transform,new Vector2(.5f,.5f),side,new Vector2((i-1)*(side+16),0));}
    }
    static void Square(RectTransform rect,Vector2 center,float side,Vector2 offset){
        rect.anchorMin=rect.anchorMax=center;rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(side,side);rect.anchoredPosition=offset;
    }
    public void Refresh(){
        if(!Ready||claimsOnly)return;HideHover();var d=service.Snapshot;var s=service.EquipmentStats;TaskService.CatLevel(d.experience,out int level,out _,out _);title.text=$"Cat Home  <size=20>Lv. {level}</size>";
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
            cell.button.onClick.AddListener(()=>OpenItem(id));cell.GetComponent<EquipmentHoverTarget>().Bind(this,id);cell.gameObject.SetActive(true);
        }
        for(int n=items.Length;n<24;n++){var cell=Instantiate(inventory.emptyTemplate,backpack);cell.name="EmptySlot";cell.SetActive(true);}
    }
    public void OpenSlot(EquipmentSlot slot){var id=EquipmentRules.Equipped(service.Snapshot,slot);if(!string.IsNullOrEmpty(id))OpenItem(id);else {notice.text="Select an owned "+EquipmentRules.SlotName(slot)+" in the Backpack.";notice.gameObject.SetActive(true);}}
    public void OpenClaim(int boss){
        if(!Ready)return;HideHover();itemDetail.gameObject.SetActive(false);modal.gameObject.SetActive(true);modal.SetAsLastSibling();choices.gameObject.SetActive(true);modalInfo.text="";modalTitle.text=EquipmentRules.BossName(boss)+" Set";
        claimView.Show(service.Snapshot,boss,service.EquipmentUnlocked);
        if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(modal.Find("Card/Close").gameObject);
    }
    public void ShowHover(EquipmentHoverTarget source,string id,Vector2 position)
    {
        if(!Ready||claimsOnly||!isActiveAndEnabled||modal.gameObject.activeSelf||confirmation.gameObject.activeSelf||
            (itemDetail.gameObject.activeSelf&&!itemDetail.IsHoverPreview))return;
        if(string.IsNullOrEmpty(id)||!service.Snapshot.equipment.Any(x=>x.id==id)){HideHover();return;}
        hoverSource=source;ShowItem(id,true,position);
    }
    public void MoveHover(EquipmentHoverTarget source,Vector2 position)
    {
        if(hoverSource==source&&itemDetail.IsHoverPreview)itemDetail.MoveHoverPreview(position);
    }
    public void HideHover(EquipmentHoverTarget source=null)
    {
        if(source&&source!=hoverSource)return;
        hoverSource=null;
        if(itemDetail&&itemDetail.IsHoverPreview)itemDetail.gameObject.SetActive(false);
    }
    void OnApplicationFocus(bool focused){if(!focused)HideHover();}
    void Update(){if(Application.IsPlaying(gameObject)&&hoverSource&&UnityEngine.InputSystem.Keyboard.current!=null&&UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)HideHover();}
    public void OpenItem(string id){HideHover();ShowItem(id,false,Vector2.zero);}
    void ShowItem(string id,bool hover,Vector2 position){
        var def=EquipmentRules.Find(id);if(def==null)return;
        var d=service.Snapshot;var item=d.equipment.FirstOrDefault(x=>x.id==id);
        bool owned=item!=null;int itemLevel=item?.level??1;
        if(!hover){selected=id;expectedLevel=itemLevel;}
        itemDetail.SetHoverPreview(hover,position);
        modal.gameObject.SetActive(false);itemDetail.gameObject.SetActive(true);itemDetail.transform.SetAsLastSibling();
        itemDetail.itemName.text=def.Name;itemDetail.tier.text=EquipmentRules.BossName(def.Tier).ToUpperInvariant()+" SET";
        itemDetail.level.text=itemLevel.ToString();itemDetail.itemIcon.sprite=slotIcons[(int)def.Slot];
        itemDetail.currentLabel.text=def.Slot==EquipmentSlot.Weapon?"Attack":def.Slot==EquipmentSlot.Body?"Defence":"Speed";
        itemDetail.currentValue.text=EquipmentRules.Effect(def,itemLevel);itemDetail.nextValue.text=EquipmentRules.Effect(def,itemLevel+1);
        int statIndex=def.Slot==EquipmentSlot.Weapon?1:def.Slot==EquipmentSlot.Body?2:3;
        if(statIcons!=null&&statIcons.Length>statIndex){itemDetail.currentIcon.sprite=statIcons[statIndex];itemDetail.nextIcon.sprite=statIcons[statIndex];}
        int set=service.EquipmentStats.SetPieces[def.Tier-1];
        itemDetail.setInfo.text=$"{EquipmentRules.BossName(def.Tier)} set";
        itemDetail.ShowSet(def.Tier,set);
        itemDetail.equip.gameObject.SetActive(!owned||!claimsOnly);
        itemDetail.upgrade.gameObject.SetActive(owned&&!claimsOnly);itemDetail.upgradeCaption.SetActive(owned&&!claimsOnly);
        itemDetail.price.text=$"{EquipmentRules.UpgradeCost(itemLevel)} Fish";
        if(owned){
            itemDetail.equipLabel.text=EquipmentRules.Equipped(d,def.Slot)==id?"Unequip":"Equip";
            itemDetail.equip.interactable=service.EquipmentUnlocked;
            string error=service.UpgradeEquipmentError(id,itemLevel);
            itemDetail.upgrade.interactable=error==null;itemDetail.status.text=error??"";
        }else{
            itemDetail.equipLabel.text="Claim";
            itemDetail.equip.interactable=service.EquipmentUnlocked&&d.bossProgress.Single(x=>x.bossId==def.Tier).pendingClaims>0;
            itemDetail.status.text=!service.EquipmentUnlocked?"Claim a Daily Reward to unlock equipment.":itemDetail.equip.interactable?"Uses 1 mastery reward. Starts at Lv. 1.":"No mastery rewards available.";
        }
        itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().SetPreview($"+{EquipmentRules.Bonus(def,itemLevel+1)-EquipmentRules.Bonus(def,itemLevel):0.##} {(def.Slot==EquipmentSlot.Weapon?"ATK":def.Slot==EquipmentSlot.Body?"DEF":"ASPD")}");
        itemDetail.close.gameObject.SetActive(!hover);
        if(hover){
            itemDetail.equip.gameObject.SetActive(false);itemDetail.upgrade.gameObject.SetActive(false);itemDetail.upgradeCaption.SetActive(false);
            itemDetail.status.text=$"Upgrade: {EquipmentRules.UpgradeCost(itemLevel)} Fish. Click item to manage.";
        }else itemDetail.FocusActions();
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
