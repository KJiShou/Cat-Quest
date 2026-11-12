using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Authored, non-interactive help for compact controls. Animation remains owned by JuicyButton.
[RequireComponent(typeof(Selectable))]
public sealed class ControlHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    public GameObject hint;
    Selectable control;
    bool hovered, focused, suppressed;
    void Awake() { control = GetComponent<Selectable>(); Hide(); }
    void Refresh()
    {
        if (!hint) return;
        bool show = control && control.IsInteractable() && !suppressed && (hovered || focused);
        hint.SetActive(show);
        if (show)
        {
            // Unity can reset an inactive nested Canvas's override during prefab loading.
            // Apply it after activation so the entire card, including TMP, stays above pages.
            var overlay = hint.GetComponent<Canvas>();
            var root = GetComponentInParent<Canvas>().rootCanvas;
            overlay.sortingLayerID = root.sortingLayerID;
            overlay.overrideSorting = true;
            overlay.sortingOrder = root.sortingOrder + 200;
        }
    }
    void Hide() { hovered = focused = suppressed = false; if (hint) hint.SetActive(false); }
    public void OnPointerEnter(PointerEventData e) { hovered = true; suppressed = false; Refresh(); }
    public void OnPointerExit(PointerEventData e) { hovered = false; Refresh(); }
    public void OnPointerDown(PointerEventData e) { suppressed = true; Refresh(); }
    public void OnSelect(BaseEventData e) { focused = !(e is PointerEventData); suppressed = false; Refresh(); }
    public void OnDeselect(BaseEventData e) { focused = false; Refresh(); }
    public void OnSubmit(BaseEventData e) { suppressed = true; Refresh(); }
    void LateUpdate() { if (hint && hint.activeSelf && (!control || !control.IsInteractable())) Hide(); }
    void OnDisable() { Hide(); }
    void OnApplicationFocus(bool focusedApp) { if (!focusedApp) Hide(); }
}
