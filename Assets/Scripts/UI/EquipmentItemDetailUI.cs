using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Linq;

// Serialized SimpleCasual detail view. Rules and actions remain in EquipmentPageUI.
[DefaultExecutionOrder(100)]
public sealed class EquipmentItemDetailUI : MonoBehaviour, IPointerClickHandler
{
    public RectTransform card;
    public TMP_Text itemName, tier, level, currentLabel, currentValue, nextValue, setInfo, status, equipLabel, price;
    public Image itemIcon, currentIcon, nextIcon, grade;
    public Button close, equip, upgrade;
    public GameObject upgradeCaption;
    public Sprite[] tierLabels;
    public TMP_Text setProgress;
    public TMP_Text[] setBonusValues, setBonusTitles;
    public Image[] setBonusCards, setProgressPips;
    public void ShowSet(int tierIndex,int pieces)
    {
        var accent=tierIndex==1?new Color32(67,164,240,255):tierIndex==2?new Color32(170,100,225,255):new Color32(235,174,42,255);
        grade.sprite=tierLabels[tierIndex-1];grade.color=Color.white;
        tier.color=tierIndex==3?new Color32(113,76,17,255):Color.white;
        setProgress.text=$"{pieces}/3 equipped";
        for(int i=0;i<setProgressPips.Length;i++)setProgressPips[i].color=i<pieces?accent:new Color32(221,226,239,255);
        setBonusValues[0].text=$"+{tierIndex*5}% HP";
        setBonusValues[1].text=$"+{tierIndex} ATK  +{tierIndex} DEF  +{tierIndex*.02:0.00} ASPD";
        for(int i=0;i<2;i++){
            bool active=pieces>=i+2;
            setBonusTitles[i].text=$"{i+2} PIECES";
            setBonusTitles[i].color=active?new Color32(15,88,153,255):new Color32(132,145,164,255);
            setBonusValues[i].color=active?new Color32(17,65,114,255):new Color32(111,123,143,255);
            setBonusCards[i].color=active?new Color32(110,205,255,255):new Color32(244,244,250,255);
        }
    }

    GameObject previousSelection;
    public bool IsHoverPreview { get; private set; }
    Vector2 pointerPosition, modalPosition;
    [SerializeField] Image backdrop;
    GraphicRaycaster raycaster;
    CanvasGroup inputGroup;
    bool cachedPresentation, backdropEnabled, raycasterEnabled, blocksRaycasts, interactable;

    // Use the authored detail card for a non-blocking preview, without owning equipment state.
    public void SetHoverPreview(bool preview, Vector2 position = default)
    {
        if(!cachedPresentation){
            cachedPresentation=true;modalPosition=card.anchoredPosition;
            raycaster=GetComponent<GraphicRaycaster>();
            inputGroup=GetComponent<CanvasGroup>();
            backdropEnabled=backdrop&&backdrop.enabled;raycasterEnabled=raycaster&&raycaster.enabled;
            blocksRaycasts=inputGroup&&inputGroup.blocksRaycasts;interactable=inputGroup&&inputGroup.interactable;
        }
        IsHoverPreview=preview;pointerPosition=position;
        if(backdrop)backdrop.enabled=!preview&&backdropEnabled;
        if(raycaster)raycaster.enabled=!preview&&raycasterEnabled;
        if(inputGroup){inputGroup.blocksRaycasts=!preview&&blocksRaycasts;inputGroup.interactable=!preview&&interactable;}
        if(!preview)card.anchoredPosition=modalPosition;
    }
    public void MoveHoverPreview(Vector2 position){pointerPosition=position;}

    public void OnPointerClick(PointerEventData eventData)
    {
        // Only a click that starts and ends outside the card dismisses the modal.
        // The full-screen raycast surface consumes the click before it reaches the page.
        if(IsHoverPreview || eventData.button != PointerEventData.InputButton.Left || eventData.dragging || !card)return;
        if(RectTransformUtility.RectangleContainsScreenPoint(card,eventData.pressPosition,eventData.pressEventCamera) ||
           RectTransformUtility.RectangleContainsScreenPoint(card,eventData.position,eventData.pressEventCamera))return;
        gameObject.SetActive(false);
    }
    void OnEnable(){if(!IsHoverPreview)previousSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;}
    void OnDisable()
    {
        var events=EventSystem.current;
        if(!IsHoverPreview&&events&&events.currentSelectedGameObject&&events.currentSelectedGameObject.transform.IsChildOf(transform))
            events.SetSelectedGameObject(previousSelection&&previousSelection.activeInHierarchy?previousSelection:null);
        if(IsHoverPreview)SetHoverPreview(false);
    }
    public void FocusActions()
    {
        var buttons=new[]{equip,upgrade,close}.Where(b=>b.gameObject.activeInHierarchy&&b.interactable).ToArray();
        for(int i=0;i<buttons.Length;i++){
            var nav=new Navigation{mode=Navigation.Mode.Explicit};
            nav.selectOnLeft=nav.selectOnUp=buttons[(i+buttons.Length-1)%buttons.Length];
            nav.selectOnRight=nav.selectOnDown=buttons[(i+1)%buttons.Length];buttons[i].navigation=nav;
        }
        if(EventSystem.current&&buttons.Length>0)EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
    }

    void LateUpdate()
    {
        var bounds = ((RectTransform)transform).rect;
        float scale = Mathf.Max(0, Mathf.Min(IsHoverPreview ? .5f : .56f, (bounds.width - 32) / card.rect.width, (bounds.height - 32) / card.rect.height));
        card.localScale = Vector3.one * scale;
        if(!IsHoverPreview)return;
        var canvas=GetComponent<Canvas>().rootCanvas;
        var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,pointerPosition,camera,out var point))return;
        Vector2 size=card.rect.size*scale;
        const float gap=20,margin=12;
        float x=point.x+gap,y=point.y-gap;
        if(x+size.x>bounds.xMax-margin)x=point.x-gap-size.x;
        if(y-size.y<bounds.yMin+margin)y=point.y+gap+size.y;
        x=Mathf.Clamp(x,bounds.xMin+margin,bounds.xMax-margin-size.x);
        y=Mathf.Clamp(y,bounds.yMin+margin+size.y,bounds.yMax-margin);
        // The authored card has centred anchors/pivot. Keep them intact for click mode.
        card.anchoredPosition=new Vector2(x+size.x*.5f,y-size.y*.5f);
    }
}
