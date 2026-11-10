using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
    readonly Dictionary<string, EquipmentInventoryItemUI> inventoryCells = new Dictionary<string, EquipmentInventoryItemUI>();
    readonly List<GameObject> emptyCells = new List<GameObject>();
    bool inventoryInitialized;
    readonly Image[] slotBackgrounds = new Image[3];
    readonly Color[] slotColors = new Color[3];
    Coroutine selectionFeedback;
    TaskDialogKeyboard modalKeyboard;
    Transform keyboardModal;
    int keyboardControls = -1;
    Button[] detailActions, claimActions, confirmationActions;
    GameObject confirmationReturnFocus;
    string selected;int expectedLevel;Action confirmAction;
    public bool Ready=>service!=null;public string SelectedItem=>selected;
    public RectTransform TutorialSlots => gear;
    public RectTransform TutorialBackpack => backpack;
    public RectTransform TutorialFilters => filters[0].transform.parent as RectTransform;
    public RectTransform TutorialAutoSelect => inventory.autoSelect.transform as RectTransform;
    static Color Pale=>new Color32(239,248,255,255);
    void Awake()
    {
        if(!Application.IsPlaying(gameObject))return;
        service=GameDataManager.EnsureInstance().Tasks;
        BindControls();
    }
    void BindControls()
    {
        var claimClose=modal.Find("Card/Close").GetComponent<Button>();
        var confirmButton=confirmation.Find("Card/Confirm").GetComponent<Button>();
        var cancelButton=confirmation.Find("Card/Cancel").GetComponent<Button>();
        detailActions=new[]{itemDetail.equip,itemDetail.upgrade,itemDetail.close};
        claimActions=new[]{claimClose}.Concat(claimView.buttons).ToArray();
        confirmationActions=new[]{cancelButton,confirmButton};
        itemDetail.Closed+=DetailClosed;
        for(int i=0;i<claimView.buttons.Length;i++){int slot=i;claimView.buttons[i].onClick.AddListener(()=>ClaimMilestone(slot));}
        claimClose.onClick.AddListener(CloseClaim);
        confirmButton.onClick.AddListener(()=>{if(confirmation.gameObject.activeInHierarchy)confirmAction?.Invoke();});
        cancelButton.onClick.AddListener(CloseConfirmation);
        itemDetail.close.onClick.AddListener(()=>itemDetail.gameObject.SetActive(false));
        itemDetail.equip.onClick.AddListener(()=>{if(service.OwnsEquipment(selected))ToggleEquip();else ConfirmClaim();});
        itemDetail.upgrade.onClick.AddListener(Upgrade);
        itemDetail.gameObject.SetActive(false);modal.gameObject.SetActive(false);confirmation.gameObject.SetActive(false);
        if(claimsOnly)return;
        for(int i=0;i<slots.Length;i++){int index=i;slotBackgrounds[i]=slots[i].GetComponent<Image>();slots[i].onClick.AddListener(()=>OpenSlot((EquipmentSlot)index));}
        for(int i=0;i<filters.Length;i++){int index=i;filters[i].onClick.AddListener(()=>{filter=index;Refresh();});}
        inventory.autoSelect.onClick.AddListener(AutoSelect);
        if(inventory.emptyAction)inventory.emptyAction.onClick.AddListener(OpenEmptyAction);
    }
    void OnEnable(){if(Ready){service.Changed+=Refresh;Refresh();}}
    void OnDisable(){if(!Application.IsPlaying(gameObject))return;StopModalKeyboard();HideHover();StopSelectionFeedback();ClearAutoSelectFeedback();if(Ready)service.Changed-=Refresh;ClearConfirmation();if(itemDetail)itemDetail.gameObject.SetActive(false);if(modal)modal.gameObject.SetActive(false);}
    void OnDestroy(){if(itemDetail)itemDetail.Closed-=DetailClosed;}
    static Color Grade(int tier)=>tier==1?new Color32(67,184,255,255):tier==2?new Color32(182,112,245,255):new Color32(255,201,65,255);
    void LateUpdate(){
        if(gear){float side=Mathf.Max(0,Mathf.Min((gear.rect.width-32)/3,gear.rect.height));for(int i=0;i<3;i++)Square((RectTransform)slots[i].transform,new Vector2(.5f,.5f),side,new Vector2((i-1)*(side+16),0));}
    }
    static void Square(RectTransform rect,Vector2 center,float side,Vector2 offset){
        rect.anchorMin=rect.anchorMax=center;rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(side,side);rect.anchoredPosition=offset;
    }
    public void Refresh(){
        if(!Ready||claimsOnly)return;HideHover();StopSelectionFeedback();ClearAutoSelectFeedback();inventory.HideAutoSelectRule(null);var d=service.EquipmentSnapshot;var s=service.EquipmentStats;TaskService.CatLevel(d.experience,out int level,out _,out _);title.text=$"Cat Home  <size=20>Lv. {level}</size>";
        notice.text="";
        inventory.autoSelect.interactable=d.equipmentUnlocked&&d.equipment.Count>0&&!service.HasActiveBattle;
        if(service.HasActiveBattle)notice.text="Finish the current battle before changing equipment.";
        for(int i=0;i<3;i++){var id=EquipmentRules.Equipped(d,(EquipmentSlot)i);var item=d.equipment.FirstOrDefault(x=>x.id==id);slotImages[i].sprite=item==null?emptySlotIcons[i]:slotIcons[i];slotImages[i].color=item==null?new Color32(161,172,198,255):Color.white;slotColors[i]=item==null?Pale:Color.Lerp(Grade(EquipmentRules.Find(id).Tier),Color.white,.75f);slotBackgrounds[i].color=slotColors[i];}
        sets.text=string.Join("  ",Enumerable.Range(1,3).Where(t=>s.SetPieces[t-1]>0).Select(t=>$"{EquipmentRules.BossName(t)} {s.SetPieces[t-1]}/3"));
        for(int i=0;i<filters.Length;i++){inventory.filterIcons[i].color=filter==i?new Color32(88,121,255,255):new Color32(193,201,208,255);inventory.filterFocus[i].SetActive(filter==i);}
        if (!inventoryInitialized) { TaskUIBuilder.Clear(backpack); inventoryInitialized=true; }
        foreach(var existing in inventoryCells)
            if(!service.OwnsEquipment(existing.Key) || !MatchesFilter(existing.Key))
                existing.Value.gameObject.SetActive(false);
        var items=d.equipment.Where(x=>MatchesFilter(x.id)).OrderBy(x=>EquipmentRules.Find(x.id).Tier).ThenBy(x=>EquipmentRules.Find(x.id).Slot).ToArray();
        UpdateEmptyState(d,items.Length==0);
        notice.gameObject.SetActive(!string.IsNullOrEmpty(notice.text));
        int position=0;
        foreach(var item in items){
            var def=EquipmentRules.Find(item.id);string id=item.id;
            if (!inventoryCells.TryGetValue(id, out var cell)) {
                cell=Instantiate(inventory.itemTemplate,backpack); cell.name="Item_"+id;
                cell.button.onClick.AddListener(()=>OpenItem(id)); cell.GetComponent<EquipmentHoverTarget>().Bind(this,id);
                inventoryCells.Add(id,cell);
            }
            cell.transform.SetSiblingIndex(position++);
            cell.background.sprite=inventory.tierBackgrounds[def.Tier-1];cell.icon.sprite=slotIcons[(int)def.Slot];
            cell.level.text=item.level.ToString();cell.equipped.SetActive(EquipmentRules.Equipped(d,def.Slot)==id);
            if(cell.upgradeAvailable)cell.upgradeAvailable.SetActive(service.UpgradeEquipmentError(id,item.level)==null);
            cell.gameObject.SetActive(true);
        }
        int emptyCount=items.Length==0?0:24-items.Length;
        while (emptyCells.Count < emptyCount) {
            var cell=Instantiate(inventory.emptyTemplate,backpack); cell.name="EmptySlot"; emptyCells.Add(cell);
        }
        for(int i=0;i<emptyCells.Count;i++){
            bool show=i<emptyCount;
            emptyCells[i].SetActive(show); if(show) emptyCells[i].transform.SetSiblingIndex(position++);
        }
    }
    bool MatchesFilter(string id)
    {
        var slot=EquipmentRules.Find(id).Slot;
        return filter==0 || (filter==1 && slot==EquipmentSlot.Head) || (filter==2 && slot==EquipmentSlot.Weapon) || (filter==3 && slot==EquipmentSlot.Body);
    }
    public void OpenSlot(EquipmentSlot slot){var id=service.EquippedItem(slot);if(!string.IsNullOrEmpty(id))OpenItem(id);else {notice.text="Select an owned "+EquipmentRules.SlotName(slot)+" in the Backpack.";notice.gameObject.SetActive(true);}}
    public void OpenClaim(int boss){
        if(!Ready)return;ClearConfirmation();HideHover();itemDetail.gameObject.SetActive(false);modal.gameObject.SetActive(true);modal.SetAsLastSibling();choices.gameObject.SetActive(true);modalInfo.text="";modalTitle.text=EquipmentRules.BossName(boss)+" Set";
        claimView.Show(service.EquipmentSnapshot,boss,service.EquipmentUnlocked);
        for(int i=0;i<claimView.buttons.Length;i++)claimView.buttons[i].GetComponent<EquipmentHoverTarget>().Bind(this,EquipmentRules.Catalog.Single(x=>x.Tier==boss&&(int)x.Slot==i).Id);
        RefreshModalKeyboard(true);
    }
    public void ShowHover(EquipmentHoverTarget source,string id,Vector2 position)
    {
        bool reward=source&&source.masteryReward&&source.transform.IsChildOf(claimView.transform)&&modal.gameObject.activeSelf;
        if(!Ready||(!reward&&(claimsOnly||modal.gameObject.activeSelf))||!isActiveAndEnabled||confirmation.gameObject.activeSelf||
            (itemDetail.gameObject.activeSelf&&!itemDetail.IsHoverPreview))return;
        if(string.IsNullOrEmpty(id)||(!reward&&!service.OwnsEquipment(id))){HideHover();return;}
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
    void OnApplicationFocus(bool focused){if(!focused){HideHover();StopSelectionFeedback();ClearAutoSelectFeedback();if(inventory)inventory.HideAutoSelectRule(null);}}
    void Update(){
        if(!Application.IsPlaying(gameObject)||!Ready)return;
        RefreshModalKeyboard();
        if(!keyboardModal&&hoverSource&&UnityEngine.InputSystem.Keyboard.current!=null&&UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)HideHover();
    }
    public void OpenItem(string id){ClearConfirmation();HideHover();ShowItem(id,false,Vector2.zero);}
    void ShowItem(string id,bool hover,Vector2 position){
        var def=EquipmentRules.Find(id);if(def==null)return;
        var d=service.EquipmentSnapshot;var item=d.equipment.FirstOrDefault(x=>x.id==id);
        bool owned=item!=null;int itemLevel=item?.level??1;
        if(!hover){selected=id;expectedLevel=itemLevel;}
        itemDetail.SetHoverPreview(hover,position);
        if(!hover)modal.gameObject.SetActive(false);itemDetail.gameObject.SetActive(true);itemDetail.transform.SetAsLastSibling();
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
            itemDetail.equip.interactable=service.ClaimEquipmentError(id)==null;
            itemDetail.status.text=!service.EquipmentUnlocked?"Claim a Daily Reward to unlock equipment.":itemDetail.equip.interactable?"Uses 1 mastery reward. Starts at Lv. 1.":"No mastery rewards available.";
        }
        itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().SetPreview($"+{EquipmentRules.Bonus(def,itemLevel+1)-EquipmentRules.Bonus(def,itemLevel):0.##} {(def.Slot==EquipmentSlot.Weapon?"ATK":def.Slot==EquipmentSlot.Body?"DEF":"ASPD")}");
        itemDetail.close.gameObject.SetActive(!hover);
        if(hover){
            itemDetail.equip.gameObject.SetActive(false);itemDetail.upgrade.gameObject.SetActive(false);itemDetail.upgradeCaption.SetActive(false);
            itemDetail.status.text=hoverSource&&hoverSource.masteryReward?(owned?"Collected":service.ClaimEquipmentError(id)??"Ready to collect"):$"Upgrade: {EquipmentRules.UpgradeCost(itemLevel)} Fish";
        }else RefreshModalKeyboard(true);
    }

    void ClaimMilestone(int slot)
    {
        if(!Ready||!claimView.buttons[slot].IsInteractable())return;
        var def=EquipmentRules.Catalog.Single(x=>x.Tier==claimView.Boss&&(int)x.Slot==slot);
        HideHover();
        bool success=service.ClaimEquipment(def.Id);
        claimView.Show(service.EquipmentSnapshot,claimView.Boss,service.EquipmentUnlocked);
        claimView.status.text=success?"Received "+def.Name+" · "+EquipmentRules.Effect(def,1):service.LastError;
        if(success)claimView.PlayCollected(slot);
        RefreshModalKeyboard(true);
    }

    void Ask(string text,Action action){if(confirmation.gameObject.activeSelf)return;itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().Dismiss();confirmationReturnFocus=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;confirmInfo.text=text;confirmAction=action;confirmation.gameObject.SetActive(true);confirmation.SetAsLastSibling();RefreshModalKeyboard(true);}
    void Result(bool success){if(success){ClearConfirmation();if(claimsOnly)OpenClaim(EquipmentRules.Find(selected).Tier);else OpenItem(selected);}else confirmInfo.text=service.LastError+"\nTry again or cancel.";}

    // One shared keyboard owner handles the top equipment surface. Underlying
    // details never process the same Escape or steal focus from a confirmation.
    void RefreshModalKeyboard(bool force=false,GameObject returnTo=null)
    {
        if(!Ready||!isActiveAndEnabled){StopModalKeyboard();return;}
        Transform top=confirmation.gameObject.activeInHierarchy?confirmation
            :itemDetail.gameObject.activeInHierarchy&&!itemDetail.IsHoverPreview?itemDetail.transform
            :modal.gameObject.activeInHierarchy?modal:null;
        if(!top){StopModalKeyboard();return;}
        var candidates=top==confirmation?confirmationActions:top==modal?claimActions:detailActions;
        int available=0;
        for(int i=0;i<candidates.Length;i++)if(candidates[i]&&candidates[i].gameObject.activeInHierarchy&&candidates[i].IsInteractable())available|=1<<i;
        if(!force&&keyboardModal==top&&keyboardControls==available)return;
        var previous=returnTo?returnTo:EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
        var controls=candidates.Where((button,index)=>(available&(1<<index))!=0).ToArray();
        if(!modalKeyboard)modalKeyboard=gameObject.AddComponent<TaskDialogKeyboard>();
        modalKeyboard.enabled=true;
        keyboardModal=top;keyboardControls=available;
        modalKeyboard.Open(controls,DismissTopModal);
        if(previous&&controls.Any(button=>button.gameObject==previous)&&EventSystem.current)EventSystem.current.SetSelectedGameObject(previous);
    }
    void StopModalKeyboard(){keyboardModal=null;keyboardControls=-1;if(modalKeyboard)modalKeyboard.enabled=false;}
    void ClearConfirmation(){confirmAction=null;confirmationReturnFocus=null;if(confirmation)confirmation.gameObject.SetActive(false);}
    void CloseConfirmation(){var previous=confirmationReturnFocus;ClearConfirmation();RefreshModalKeyboard(true,previous);}
    void CloseClaim(){HideHover();modal.gameObject.SetActive(false);RefreshModalKeyboard(true);}
    void DetailClosed(){ClearConfirmation();RefreshModalKeyboard(true);}
    void DismissTopModal()
    {
        if(hoverSource){HideHover();return;}
        if(confirmation.gameObject.activeInHierarchy)CloseConfirmation();
        else if(itemDetail.gameObject.activeInHierarchy&&!itemDetail.IsHoverPreview)itemDetail.gameObject.SetActive(false);
        else if(modal.gameObject.activeInHierarchy)CloseClaim();
    }
    void ToggleEquip(){
        var def=EquipmentRules.Find(selected);string id=selected;
        bool worn=service.EquippedItem(def.Slot)==id;
        bool success=worn?service.Unequip(def.Slot):service.Equip(id,def.Slot);
        if(success){Result(true);itemDetail.status.text=worn?"Unequipped.":"Equipped.";}
        else itemDetail.status.text=service.LastError;
    }
    void AutoSelect(){
        StopSelectionFeedback();ClearAutoSelectFeedback();inventory.HideAutoSelectRule(null);
        var previous=service.EquipmentSnapshot;
        if(!service.AutoEquip()){notice.text=service.LastError;notice.gameObject.SetActive(true);return;}
        // Feedback follows the committed loadout; the existing stat rows show its values.
        var current=service.EquipmentSnapshot;
        int mask=0;
        for(int i=0;i<3;i++)if(EquipmentRules.Equipped(previous,(EquipmentSlot)i)!=EquipmentRules.Equipped(current,(EquipmentSlot)i))mask|=1<<i;
        notice.text="";notice.gameObject.SetActive(false);
        if(inventory.autoSelectFeedback)inventory.autoSelectFeedback.SetActive(true);
        if(inventory.autoSelectPowerDelta){
            inventory.autoSelectPowerDelta.text=mask==0?"":Delta(EquipmentRules.Calculate(current).Final.Power-EquipmentRules.Calculate(previous).Final.Power);
            inventory.autoSelectPowerDelta.transform.parent.gameObject.SetActive(mask!=0);
        }
        if(mask!=0&&!UiMotion.Reduced)selectionFeedback=StartCoroutine(HighlightSelection(mask));
    }
    void ClearAutoSelectFeedback(){
        if(!inventory)return;
        if(inventory.autoSelectFeedback)inventory.autoSelectFeedback.SetActive(false);
        if(inventory.autoSelectPowerDelta){inventory.autoSelectPowerDelta.text="";inventory.autoSelectPowerDelta.transform.parent.gameObject.SetActive(false);}
    }
    static string Delta(double value)=>value.ToString("+0.##;-0.##;0",System.Globalization.CultureInfo.InvariantCulture);
    IEnumerator HighlightSelection(int mask)
    {
        float elapsed=0;
        while(elapsed<1.2f&&!UiMotion.Reduced){
            float amount=.6f*(1-Mathf.Clamp01(elapsed/1.2f));
            for(int i=0;i<3;i++)if((mask&(1<<i))!=0)slotBackgrounds[i].color=Color.Lerp(slotColors[i],new Color32(255,208,75,255),amount);
            elapsed+=Time.unscaledDeltaTime;yield return null;
        }
        for(int i=0;i<3;i++)slotBackgrounds[i].color=slotColors[i];
        selectionFeedback=null;
    }
    void StopSelectionFeedback()
    {
        if(selectionFeedback==null)return;
        StopCoroutine(selectionFeedback);selectionFeedback=null;
        for(int i=0;i<3;i++)if(slotBackgrounds[i])slotBackgrounds[i].color=slotColors[i];
    }
    int ReadyClaimBoss(PlayerData data)=>data.bossProgress.Where(progress=>progress.pendingClaims>0)
        .Select(progress=>progress.bossId).FirstOrDefault(boss=>EquipmentRules.Catalog.Any(def=>def.Tier==boss&&service.ClaimEquipmentError(def.Id)==null));
    void UpdateEmptyState(PlayerData data,bool empty)
    {
        if(!inventory.emptyState)return;
        inventory.emptyState.SetActive(empty);
        if(inventory.emptyMessage)inventory.emptyMessage.gameObject.SetActive(false);
        if(!empty)return;
        int icon;string threshold="",action;
        if(data.equipment.Count>0){icon=0;action="All equipment";}
        else if(!data.equipmentUnlocked){icon=1;action="Daily Rewards";}
        else if(!service.CanAccess(ProgressionFeature.Boss)){icon=2;threshold="Lv.5";action="Tasks";}
        else if(ReadyClaimBoss(data)>0){icon=3;action="Claim";}
        else{
            icon=4;action="Boss";
            int wins=data.bossProgress.FirstOrDefault(progress=>progress.bossId==1)?.wins??0;
            threshold=$"{Mathf.Clamp(wins,0,5)} / 5 wins";
        }
        if(inventory.emptyIcon&&inventory.emptyStateIcons!=null&&icon<inventory.emptyStateIcons.Length){
            inventory.emptyIcon.sprite=inventory.emptyStateIcons[icon];
            inventory.emptyIcon.color=icon==0||icon==2?TaskUIBuilder.Ink:Color.white;
        }
        inventory.emptyHeading.text=threshold;
        inventory.emptyHeading.gameObject.SetActive(!string.IsNullOrEmpty(threshold));
        inventory.emptyActionLabel.text=action;
    }
    void OpenEmptyAction()
    {
        var data=service.EquipmentSnapshot;
        if(data.equipment.Count>0){filter=0;Refresh();return;}
        if(!navigation)return;
        if(!data.equipmentUnlocked){navigation.ShowPage(MainMenuPage.Dashboard);return;}
        if(!service.CanAccess(ProgressionFeature.Boss)){navigation.ShowPage(MainMenuPage.Tasks);return;}
        int claim=ReadyClaimBoss(data);
        if(navigation.ShowPage(MainMenuPage.Boss)&&claim>0)navigation.pages[(int)MainMenuPage.Boss].GetComponent<EquipmentPageUI>().OpenClaim(claim);
    }
    void Upgrade(){
        if(!itemDetail.upgrade.interactable)return;
        itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().Dismiss();
        bool success=service.UpgradeEquipment(selected,expectedLevel,Guid.NewGuid().ToString("N"));
        if(success){Result(true);itemDetail.status.text="Upgraded.";itemDetail.upgrade.GetComponent<EquipmentUpgradeFeedback>().PlaySuccess();}
        else {string error=service.LastError;OpenItem(selected);itemDetail.status.text=error;}
    }
    void ConfirmClaim(){string id=selected;Ask("Claim "+EquipmentRules.Find(id).Name+"?\nUses 1 mastery reward. Starts at Lv. 1.",()=>Result(service.ClaimEquipment(id)));}
}
