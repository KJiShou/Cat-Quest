using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed class LootShopItemUI : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler, IPointerDownHandler, ISelectHandler, IDeselectHandler
{
    public Image icon, background;
    public TMP_Text title, description;
    public Image slotIcon, fishIcon;
    public Image ownedIcon;
    public CanvasGroup visibility;
    public Button button;
    LootShopUI owner;
    string itemId;
    public void Bind(LootShopUI shop,string id){owner=shop;itemId=id;}
    public void SetOwned(bool owned){if(ownedIcon)ownedIcon.gameObject.SetActive(owned);}
    public void OnPointerEnter(PointerEventData e){owner?.ShowHover(this,itemId,e.position);}
    public void OnPointerMove(PointerEventData e){owner?.MoveHover(this,e.position);}
    public void OnPointerExit(PointerEventData e){owner?.HideHover(this);}
    public void OnPointerDown(PointerEventData e){owner?.HideHover(this);}
    public void OnSelect(BaseEventData e){if(!(e is PointerEventData))owner?.ShowHover(this,itemId,RectTransformUtility.WorldToScreenPoint(null,transform.position));}
    public void OnDeselect(BaseEventData e){owner?.HideHover(this);}
    void OnDisable(){owner?.HideHover(this);}
}
