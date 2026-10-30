using UnityEngine;
using UnityEngine.EventSystems;

// The authored currency pill remains the hit area; the tooltip never receives input.
public enum RewardLimitKind { Fish, Tickets, Experience }

public sealed class RewardLimitHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, ISelectHandler, IDeselectHandler
{
    public RewardLimitTooltip tooltip;
    public RewardLimitKind kind;
    public void OnPointerEnter(PointerEventData eventData) { if (tooltip) tooltip.Show(this); }
    public void OnPointerExit(PointerEventData eventData) { if (tooltip) tooltip.Hide(this); }
    public void OnPointerDown(PointerEventData eventData) { if (tooltip) tooltip.Hide(this); }
    public void OnSelect(BaseEventData eventData) { if (tooltip) tooltip.Show(this); }
    public void OnDeselect(BaseEventData eventData) { if (tooltip) tooltip.Hide(this); }
    void OnDisable() { if (tooltip) tooltip.Hide(this); }
}
