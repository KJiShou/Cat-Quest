using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Linq;

// Serialized SimpleCasual detail view. Rules and actions remain in EquipmentPageUI.
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
            setBonusTitles[i].text=$"{i+2} PIECES"+(active?"  • ACTIVE":"");
            setBonusTitles[i].color=active?accent:new Color32(132,145,164,255);
            setBonusCards[i].color=active?Color.Lerp(accent,Color.white,.86f):new Color32(244,244,250,255);
        }
    }

    GameObject previousSelection;
    public void OnPointerClick(PointerEventData eventData)
    {
        // Only a click that starts and ends outside the card dismisses the modal.
        // The full-screen raycast surface consumes the click before it reaches the page.
        if(eventData.button != PointerEventData.InputButton.Left || eventData.dragging || !card)return;
        if(RectTransformUtility.RectangleContainsScreenPoint(card,eventData.pressPosition,eventData.pressEventCamera) ||
           RectTransformUtility.RectangleContainsScreenPoint(card,eventData.position,eventData.pressEventCamera))return;
        gameObject.SetActive(false);
    }
    void OnEnable(){previousSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;}
    void OnDisable()
    {
        var events=EventSystem.current;
        if(events&&events.currentSelectedGameObject&&events.currentSelectedGameObject.transform.IsChildOf(transform))
            events.SetSelectedGameObject(previousSelection&&previousSelection.activeInHierarchy?previousSelection:null);
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
        float scale = Mathf.Max(0, Mathf.Min(.56f, (bounds.width - 32) / card.rect.width, (bounds.height - 32) / card.rect.height));
        card.localScale = Vector3.one * scale;
    }
}
