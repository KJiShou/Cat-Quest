using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// All surfaces/templates are authored in the project prefab. This is presentation only.
public sealed class LootShopUI : MonoBehaviour
{
    public LootArt art;
    public RectTransform panel;
    public Button close, single, ten, skip, done, confirm, cancel;
    public TMP_Text balance, status, confirmationText, resultTitle;
    public GameObject confirmation, reveal;
    public Image chest;
    public RectTransform results;
    public LootShopItemUI resultTemplate;
    public LootShopItemUI[] catalog;
    public float openingSeconds=1.8f;
    public float chestFrameScale=.65f;
    public Button information, informationClose;
    public GameObject informationPanel;
    public TMP_Text informationText;
    public TMP_Text[] rarityRates, duplicateValues;
    public TMP_Text confirmCost;
    public EquipmentItemDetailUI hoverDetail;
    public Sprite[] slotIcons, statIcons;
    public Image flyingIcon;
    public float flightSeconds=.48f, tenFlightSeconds=.24f, flightGap=.035f;
    public int ChestFrameIndex {get;private set;}
    public Vector2 chestCanvasOffset=new Vector2(-470,-35);
    readonly List<LootShopItemUI> revealedItems=new List<LootShopItemUI>();
    LootShopItemUI hoverSource;
    public int RevealedCount {get;private set;}
    TaskService service;
    TaskDialogKeyboard keyboard;
    Coroutine animation;
    LootReceipt shown;
    int pendingCount;
    string requestId;
    bool busy;
    bool playRewardSound;
    public bool Revealing => animation!=null;
    void Awake(){
        service=GameDataManager.EnsureInstance().Tasks;
        keyboard=GetComponent<TaskDialogKeyboard>();
        close.onClick.AddListener(Close);
        single.onClick.AddListener(()=>Prepare(1));ten.onClick.AddListener(()=>Prepare(10));
        confirm.onClick.AddListener(Purchase);cancel.onClick.AddListener(Cancel);
        skip.onClick.AddListener(FinishReveal);done.onClick.AddListener(CloseResults);
        information.onClick.AddListener(OpenInformation);informationClose.onClick.AddListener(CloseInformation);
        for(int row=0;row<3;row++){
            int tier=3-row;rarityRates[row].text=LootRules.Percent(tier)+"%";
            duplicateValues[row].text="+"+LootRules.DuplicateFish(tier);
        }
        for(int i=0;i<catalog.Length;i++){
            var def=LootRules.Catalog[i];var card=catalog[i];
            card.icon.sprite=art.Icon(def.Id);card.title.text=def.Name;
            card.Bind(this,def.Id);card.slotIcon.sprite=slotIcons[(int)def.Slot];
            card.description.text="";
        }
    }
    void OnEnable(){if(service==null)return;service.Changed+=Refresh;Refresh();FocusMain();}
    void OnDisable(){
        if(service!=null)service.Changed-=Refresh;
        HideHover();StopAnimation();confirmation.SetActive(false);reveal.SetActive(false);informationPanel.SetActive(false);busy=false;pendingCount=0;requestId=null;playRewardSound=false;
        if(keyboard)keyboard.enabled=false;
    }
    void OnApplicationFocus(bool focused){if(!focused){HideHover();if(animation!=null)FinishReveal();}}
    void LateUpdate(){if(panel){var bounds=((RectTransform)transform).rect;panel.localScale=Vector3.one*Mathf.Min(1,(bounds.width-32)/1600,(bounds.height-32)/900);}}
    void Refresh(){
        balance.text=service.Tickets.ToString();
        for(int i=0;i<catalog.Length;i++)catalog[i].SetOwned(service.OwnsEquipment(LootRules.Catalog[i].Id));
        single.interactable=!busy&&service.LootError(1)==null;
        ten.interactable=!busy&&service.LootError(10)==null;
        if(!busy)status.text=service.LootError(1)??"";
    }
    void FocusMain(){
        keyboard.enabled=true;keyboard.Open(new[]{close,information,single,ten}.Where(b=>b&&b.interactable).ToArray(),Close);
    }
    public void OpenInformation(){if(busy)return;HideHover();informationPanel.SetActive(true);keyboard.Open(new[]{informationClose}.Concat(catalog.Select(c=>c.button)).ToArray(),CloseInformation);}
    public void CloseInformation(){HideHover();informationPanel.SetActive(false);FocusMain();}
    public void ShowHover(LootShopItemUI source,string id,Vector2 position){
        if(!isActiveAndEnabled||(busy&&(!reveal.activeSelf||Revealing))||!source||!source.gameObject.activeInHierarchy)return;
        var def=EquipmentRules.Find(id);if(def==null)return;
        hoverSource=source;var item=service.EquipmentSnapshot.equipment.FirstOrDefault(e=>e.id==id);int level=item?.level??1;
        hoverDetail.SetHoverPreview(true,position);hoverDetail.gameObject.SetActive(true);
        hoverDetail.itemName.text=def.Name;hoverDetail.itemIcon.sprite=art.Icon(id);hoverDetail.level.text=level.ToString();
        hoverDetail.ShowSet(def.Tier,0);hoverDetail.ShowAbility(def);hoverDetail.tier.text=LootRules.Rarity(def.Tier);
        hoverDetail.currentLabel.text=def.Slot==EquipmentSlot.Weapon?"Attack":def.Slot==EquipmentSlot.Body?"Defence":"Speed";
        hoverDetail.currentIcon.sprite=statIcons[def.Slot==EquipmentSlot.Weapon?1:def.Slot==EquipmentSlot.Body?2:3];hoverDetail.currentValue.text=EquipmentRules.Effect(def,level);
        hoverDetail.setInfo.text="";hoverDetail.setProgress.text="";hoverDetail.status.text=item==null?"":"Owned";
        foreach(var card in hoverDetail.setBonusCards)card.gameObject.SetActive(false);
        foreach(var pip in hoverDetail.setProgressPips)pip.gameObject.SetActive(false);
        hoverDetail.equip.gameObject.SetActive(false);hoverDetail.upgrade.gameObject.SetActive(false);hoverDetail.upgradeCaption.SetActive(false);hoverDetail.close.gameObject.SetActive(false);
    }
    public void MoveHover(LootShopItemUI source,Vector2 position){if(hoverSource==source)hoverDetail.MoveHoverPreview(position);}
    public void HideHover(LootShopItemUI source=null){if(source&&source!=hoverSource)return;hoverSource=null;if(hoverDetail)hoverDetail.gameObject.SetActive(false);}
    public void Prepare(int count){
        if(busy)return;
        HideHover();informationPanel.SetActive(false);
        var error=service.LootError(count);if(error!=null){status.text=error;return;}
        pendingCount=count;requestId=Guid.NewGuid().ToString("N");busy=true;Refresh();
        confirmationText.text=$"Open {count} chest{(count==1?"":"s")}?";
        confirmCost.text=count.ToString();
        confirmation.SetActive(true);keyboard.Open(new[]{cancel,confirm},Cancel);
    }
    void Cancel(){confirmation.SetActive(false);busy=false;pendingCount=0;requestId=null;Refresh();FocusMain();}
    public void Purchase(){
        if(!confirmation.activeSelf||pendingCount==0||!confirm.interactable)return;
        confirm.interactable=false;
        try {
            if(!service.DrawLoot(pendingCount,requestId,out var receipt)){
                confirmationText.text=service.LastError+"\nRetry or cancel. No chest result has been delivered.";return;
            }
            pendingCount=0;requestId=null;confirmation.SetActive(false);Show(receipt,true);
        } finally {confirm.interactable=true;}
    }
    void Show(LootReceipt receipt,bool animate){
        if(receipt==null)return;
        HideHover();shown=receipt;busy=true;Refresh();StopAnimation();playRewardSound=animate;RevealedCount=0;
        foreach(Transform child in results){child.gameObject.SetActive(false);Destroy(child.gameObject);}revealedItems.Clear();
        foreach(var drop in receipt.drops){
            var def=EquipmentRules.Find(drop.itemId);var cell=Instantiate(resultTemplate,results);
            cell.icon.sprite=art.Icon(def.Id);cell.title.text=def.Name;cell.Bind(this,def.Id);
            cell.background.color=Tint(def.Tier);
            cell.description.text=drop.duplicateFish>0?"+"+drop.duplicateFish:"NEW";
            cell.fishIcon.gameObject.SetActive(drop.duplicateFish>0);cell.slotIcon.sprite=slotIcons[(int)def.Slot];
            cell.visibility.alpha=0;cell.visibility.interactable=false;cell.visibility.blocksRaycasts=false;
            cell.gameObject.SetActive(true);revealedItems.Add(cell);
        }
        resultTitle.text="";
        reveal.SetActive(true);results.gameObject.SetActive(true);done.gameObject.SetActive(false);skip.gameObject.SetActive(true);chest.gameObject.SetActive(true);
        keyboard.Open(new[]{skip},FinishReveal);
        if(animate&&!UiMotion.Reduced)animation=StartCoroutine(OpenChest());else FinishReveal();
    }
    void UpdateChestFrame(Sprite[] frames,float age){
        ChestFrameIndex=Mathf.Min(frames.Length-1,(int)(age/openingSeconds*frames.Length));
        if(ChestFrameIndex>=0)ApplyChestFrame(frames[ChestFrameIndex]);
    }
    IEnumerator OpenChest(){
        var frames=art.Opening(shown.drops.Max(d=>EquipmentRules.Find(d.itemId).Tier));
        float started=Time.unscaledTime;
        float releaseTime=openingSeconds*art.releaseFrame/Mathf.Max(1,frames.Length);
        while(Time.unscaledTime-started<releaseTime){
            UpdateChestFrame(frames,Time.unscaledTime-started);yield return null;
        }
        Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(results);
        float duration=shown.count==10?tenFlightSeconds:flightSeconds;
        for(int i=0;i<revealedItems.Count;i++){
            var cell=revealedItems[i];flyingIcon.sprite=cell.icon.sprite;flyingIcon.gameObject.SetActive(true);
            var parent=(RectTransform)flyingIcon.transform.parent;
            Vector3 start=chestCanvasOffset+new Vector2(0,-40);
            var target=parent.InverseTransformPoint(cell.icon.rectTransform.TransformPoint(cell.icon.rectTransform.rect.center));
            float flightStarted=Time.unscaledTime;
            while(Time.unscaledTime-flightStarted<duration){
                UpdateChestFrame(frames,Time.unscaledTime-started);
                float t=Mathf.Clamp01((Time.unscaledTime-flightStarted)/duration),ease=t*t*(3-2*t);
                var point=Vector3.Lerp(start,target,ease);point.y+=Mathf.Sin(t*Mathf.PI)*150;
                flyingIcon.rectTransform.localPosition=point;flyingIcon.rectTransform.localScale=Vector3.one*Mathf.Lerp(.35f,1,ease);
                yield return null;
            }
            flyingIcon.gameObject.SetActive(false);cell.visibility.alpha=1;RevealedCount++;
            cell.icon.GetComponent<JuicyButtonMesh>()?.PlayRewardPulse();
            if(playRewardSound)SettingsAudio.Instance?.PlayEquipmentClaim();
            float landed=Time.unscaledTime;
            while(Time.unscaledTime-landed<flightGap){UpdateChestFrame(frames,Time.unscaledTime-started);yield return null;}
        }
        while(Time.unscaledTime-started<openingSeconds){UpdateChestFrame(frames,Time.unscaledTime-started);yield return null;}
        playRewardSound=false;
        animation=null;FinishReveal();
    }
    void ApplyChestFrame(Sprite frame){
        chest.sprite=frame;chest.preserveAspect=false;chest.raycastTarget=false;
        var rect=chest.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.sizeDelta=frame.rect.size*chestFrameScale;
        // The imported sprites are trimmed. Reconstruct their position on the
        // original PNG canvas instead of stretching each trim to the same box.
        var sourceCenter=art.frameCanvasSize*.5f;
        rect.anchoredPosition=chestCanvasOffset+(frame.rect.center-sourceCenter)*chestFrameScale;
    }
    void StopAnimation(){if(animation!=null)StopCoroutine(animation);animation=null;if(flyingIcon){flyingIcon.gameObject.SetActive(false);flyingIcon.rectTransform.localScale=Vector3.one;}foreach(var item in revealedItems)if(item)item.icon.GetComponent<JuicyButtonMesh>()?.ResetRewardPulse();}
    public void FinishReveal(){
        StopAnimation();skip.gameObject.SetActive(false);results.gameObject.SetActive(true);done.gameObject.SetActive(true);
        var frames=art.Opening(shown.drops.Max(d=>EquipmentRules.Find(d.itemId).Tier));if(frames.Length>0)ApplyChestFrame(frames[frames.Length-1]);chest.gameObject.SetActive(true);
        foreach(var item in revealedItems){item.visibility.alpha=1;item.visibility.interactable=true;item.visibility.blocksRaycasts=true;}RevealedCount=revealedItems.Count;
        if(playRewardSound){playRewardSound=false;SettingsAudio.Instance?.PlayEquipmentClaim();}
        keyboard.Open(new[]{done}.Concat(revealedItems.Select(c=>c.button)).ToArray(),CloseResults);
    }
    void CloseResults(){HideHover();StopAnimation();reveal.SetActive(false);busy=false;Refresh();FocusMain();}
    public void Close(){if(hoverSource){HideHover();return;}if(informationPanel.activeSelf){CloseInformation();return;}if(confirmation.activeSelf){Cancel();return;}if(reveal.activeSelf){if(Revealing)FinishReveal();else CloseResults();return;}gameObject.SetActive(false);}
    public static Color Tint(int tier)=>tier==3?new Color32(255,237,170,255):tier==2?new Color32(235,212,255,255):new Color32(187,240,255,255);
}
