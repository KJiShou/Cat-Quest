using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Authored help for compact controls. JuicyButton owns press/hover feedback;
// optional inline labels animate width without moving the control's icon column.
[RequireComponent(typeof(Selectable))]
public sealed class ControlHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    public GameObject hint;
    // Optional inline reveal. JuicyButton still owns hover/press feedback; this
    // only reveals the authored label beside a stationary icon hit area.
    public RectTransform expansion;
    public CanvasGroup expansionLabel;
    public float collapsedWidth=48, expandedWidth=192, expansionDuration=.24f;
    Selectable control;
    bool hovered, focused, suppressed;
    float fromWidth,targetWidth,elapsed;
    void Awake() { control = GetComponent<Selectable>(); Hide(); }
    void Refresh()
    {
        if(expansion) {
            bool reveal=control && control.IsInteractable() && !suppressed && (hovered || focused);
            float next=reveal?expandedWidth:collapsedWidth;
            if(!Mathf.Approximately(targetWidth,next)){fromWidth=expansion.rect.width;targetWidth=next;elapsed=0;}
            return;
        }
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
    void Hide() { hovered = focused = suppressed = false; if (hint) hint.SetActive(false);
        if(expansion){fromWidth=targetWidth=collapsedWidth;elapsed=expansionDuration;ApplyWidth(collapsedWidth);}
    }
    void ApplyWidth(float width) {
        expansion.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
        if(expansionLabel)expansionLabel.alpha=Mathf.InverseLerp(collapsedWidth,expandedWidth,width);
    }
    public void OnPointerEnter(PointerEventData e) { hovered = true; suppressed = false; Refresh(); }
    public void OnPointerExit(PointerEventData e) { hovered = false; Refresh(); }
    public void OnPointerDown(PointerEventData e) {
        // Inline labels are part of the button: shrinking on press could remove
        // the release target underneath a pointer clicking on the revealed text.
        if(expansion)return;
        suppressed = true; Refresh();
    }
    public void OnSelect(BaseEventData e) { focused = !(e is PointerEventData); suppressed = false; Refresh(); }
    public void OnDeselect(BaseEventData e) { focused = false; Refresh(); }
    public void OnSubmit(BaseEventData e) { suppressed = true; Refresh(); }
    void LateUpdate() {
        if(expansion){
            if(!control || !control.IsInteractable()){Hide();return;}
            elapsed+=Time.unscaledDeltaTime;
            float t=UiMotion.Reduced||expansionDuration<=0?1:Mathf.Clamp01(elapsed/expansionDuration);
            ApplyWidth(Mathf.Lerp(fromWidth,targetWidth,Mathf.SmoothStep(0,1,t)));
        } else if (hint && hint.activeSelf && (!control || !control.IsInteractable())) Hide();
    }
    void OnDisable() { Hide(); }
    void OnApplicationFocus(bool focusedApp) { if (!focusedApp) Hide(); }
}
