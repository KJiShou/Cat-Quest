using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Authored lock artwork; no page or navigation hierarchy is rebuilt at runtime.
public sealed class FeatureLockUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public ProgressionFeature feature;
    public GameObject artwork, tooltip;
    public TMP_Text reason;
    public bool card;
    Selectable[] controls;
    bool[] enabledBeforeLock;
    bool wasLocked;
    TaskService service;
    FocusController focus;
    SidebarNavigationButton navigation;
    bool hovered, selected;
    void OnEnable()
    {
        navigation = GetComponent<SidebarNavigationButton>();
        service = GameDataManager.EnsureInstance().Tasks;
        focus = FocusRunner.EnsureInstance().Controller;
        if(card && controls == null) { controls = GetComponentsInChildren<Selectable>(true); enabledBeforeLock = new bool[controls.Length]; }
        service.Changed += Refresh; focus.Changed += Refresh; Refresh();
    }
    void OnDisable()
    {
        if(service != null) service.Changed -= Refresh;
        if(focus != null) focus.Changed -= Refresh;
        hovered = selected = false;
        if(tooltip) tooltip.SetActive(false);
    }
    public bool Locked => service != null && !service.CanAccess(feature)
        && !(feature == ProgressionFeature.Focus && focus.Phase is FocusPhase.Focusing or FocusPhase.Saving or FocusPhase.Resting)
        && !(feature == ProgressionFeature.Boss && service.HasActiveBattle);
    void Refresh()
    {
        bool locked = Locked;
        artwork.SetActive(locked);
        reason.text = (navigation && !string.IsNullOrEmpty(navigation.NavigationName) ? navigation.NavigationName + "\n" : "") + service.AccessMessage(feature);
        if(card && locked != wasLocked)
        {
            for(int i=0;i<controls.Length;i++) if(controls[i])
            {
                if(locked) { enabledBeforeLock[i] = controls[i].interactable; controls[i].interactable = false; }
                else controls[i].interactable = enabledBeforeLock[i];
            }
        }
        wasLocked = locked;
        Tip();
        if(navigation) navigation.RefreshNameTooltip();
    }
    void Tip() { if(tooltip) { bool show=(hovered || selected) && Locked; tooltip.SetActive(show); if(show) tooltip.transform.SetAsLastSibling(); } }
    public void OnPointerEnter(PointerEventData e) { hovered=true; Tip(); }
    public void OnPointerExit(PointerEventData e) { hovered=false; Tip(); }
    public void OnSelect(BaseEventData e) { selected=!(e is PointerEventData); Tip(); }
    public void OnDeselect(BaseEventData e) { selected=false; Tip(); }
    void OnApplicationFocus(bool focused) { if(!focused) { hovered=selected=false; Tip(); } }
}
