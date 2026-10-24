using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shared pointer adapter for authored equipped slots and reusable inventory cells.
[RequireComponent(typeof(Button))]
public sealed class EquipmentHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler, IPointerDownHandler
{
    public EquipmentPageUI owner;
    public bool equippedSlot;
    public EquipmentSlot slot;
    string itemId;
    Button button;
    void Awake(){button=GetComponent<Button>();}
    public void Bind(EquipmentPageUI page,string id){owner=page;itemId=id;}
    public void OnPointerEnter(PointerEventData data)
    {
        if(!owner||!owner.Ready||!button||!button.IsInteractable())return;
        string id=equippedSlot?EquipmentRules.Equipped(GameDataManager.Instance.Tasks.Snapshot,slot):itemId;
        owner.ShowHover(this,id,data.position);
    }
    public void OnPointerMove(PointerEventData data){if(owner)owner.MoveHover(this,data.position);}
    public void OnPointerDown(PointerEventData data){if(owner)owner.HideHover(this);}
    public void OnPointerExit(PointerEventData data){if(owner)owner.HideHover(this);}
    void OnDisable(){if(owner)owner.HideHover(this);}
}
