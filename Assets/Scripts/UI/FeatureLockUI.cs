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
    void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        focus = FocusRunner.EnsureInstance().Controller;
        if(card && controls == null) { controls = GetComponentsInChildren<Selectable>(true); enabledBeforeLock = new bool[controls.Length]; }
        service.Changed += Refresh; focus.Changed += Refresh; Refresh();
    }
    void OnDisable()
    {
        if(service != null) service.Changed -= Refresh;
        if(focus != null) focus.Changed -= Refresh;
        if(tooltip) tooltip.SetActive(false);
    }
    public bool Locked => service != null && !service.CanAccess(feature)
        && !(feature == ProgressionFeature.Focus && focus.Phase is FocusPhase.Focusing or FocusPhase.Saving or FocusPhase.Resting)
        && !(feature == ProgressionFeature.Boss && service.Snapshot.battles.Exists(b => b.outcome == BattleOutcome.Running));
    void Refresh()
    {
        bool locked = Locked;
        artwork.SetActive(locked); reason.text = service.AccessMessage(feature);
        if(card && locked != wasLocked)
        {
            for(int i=0;i<controls.Length;i++) if(controls[i])
            {
                if(locked) { enabledBeforeLock[i] = controls[i].interactable; controls[i].interactable = false; }
                else controls[i].interactable = enabledBeforeLock[i];
            }
        }
        wasLocked = locked;
        if(tooltip && !Locked) tooltip.SetActive(false);
    }
    void Tip(bool show) { if(tooltip) { tooltip.SetActive(show && Locked); if(show) tooltip.transform.SetAsLastSibling(); } }
    public void OnPointerEnter(PointerEventData e) => Tip(true);
    public void OnPointerExit(PointerEventData e) => Tip(false);
    public void OnSelect(BaseEventData e) => Tip(true);
    public void OnDeselect(BaseEventData e) => Tip(false);
}
