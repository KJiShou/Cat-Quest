using UnityEngine;
using UnityEngine.EventSystems;

// Input adapter for the shared Cat Home detail view, using a published loadout only.
public sealed class LeaderboardEquipmentHover : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler,
    IPointerExitHandler, IPointerDownHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    public LeaderboardPlayerUI owner;
    public EquipmentSlot slot;
    bool focused;
    public void OnPointerEnter(PointerEventData e) { owner.ShowEquipment(this,e.position); }
    public void OnPointerMove(PointerEventData e) { owner.MoveEquipment(this,e.position); }
    public void OnPointerExit(PointerEventData e) { if(!focused)owner.HideEquipment(this); }
    public void OnPointerDown(PointerEventData e) { focused=false;owner.HideEquipment(this); }
    public void OnSelect(BaseEventData e)
    {
        focused=!(e is PointerEventData);
        if(focused)owner.ShowEquipment(this,RectTransformUtility.WorldToScreenPoint(null,transform.position));
    }
    public void OnDeselect(BaseEventData e) { focused=false;owner.HideEquipment(this); }
    public void OnSubmit(BaseEventData e) { focused=false;owner.HideEquipment(this); }
    void OnDisable() { focused=false;if(owner)owner.HideEquipment(this); }
}
