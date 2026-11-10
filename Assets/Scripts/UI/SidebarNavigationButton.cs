using UnityEngine.UI;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

// A page selection survives EventSystem focus moving to controls inside that page.
public sealed class SidebarNavigationButton : Button
{
    private bool pageSelectionManaged;
    private bool pageSelected;
    // Only legacy animated navigation buttons need this retained selection styling.
    [SerializeField] private TMP_Text selectionLabel;
    [SerializeField] private Color selectedBackgroundColor = Color.white;
    [SerializeField] private Color selectedLabelColor = Color.white;
    [SerializeField, HideInInspector] private Color normalBackgroundColor = Color.white;
    [SerializeField, HideInInspector] private Color normalLabelColor = Color.white;
    [SerializeField, HideInInspector] private bool normalAppearanceCaptured;
    [SerializeField] private Toggle expandedToggle;
    [SerializeField] private GameObject navigationTooltip;
    [SerializeField] private TMP_Text navigationTooltipLabel;
    [SerializeField] private string pageName;
    private FeatureLockUI featureLock;
    private bool pointerOver, keyboardFocused, tooltipSuppressed;
    public string NavigationName => pageName;

    protected override void Awake()
    {
        CaptureNormalAppearance();
        featureLock = GetComponent<FeatureLockUI>();
        base.Awake();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (expandedToggle) expandedToggle.onValueChanged.AddListener(ExpansionChanged);
        RefreshNameTooltip();
    }

    protected override void OnDisable()
    {
        if (expandedToggle) expandedToggle.onValueChanged.RemoveListener(ExpansionChanged);
        ResetTooltip();
        base.OnDisable();
    }

    void ExpansionChanged(bool expanded) => RefreshNameTooltip();
    public void RefreshNameTooltip()
    {
        if (!navigationTooltip) return;
        bool show = isActiveAndEnabled && expandedToggle && !expandedToggle.isOn
            && (pointerOver || keyboardFocused) && !tooltipSuppressed && (!featureLock || !featureLock.Locked);
        if (navigationTooltipLabel) navigationTooltipLabel.text = pageName;
        navigationTooltip.SetActive(show);
    }
    void ResetTooltip()
    {
        pointerOver = keyboardFocused = tooltipSuppressed = false;
        if (navigationTooltip) navigationTooltip.SetActive(false);
    }
    void OnApplicationFocus(bool focused) { if (!focused) ResetTooltip(); }
    public override void OnPointerEnter(PointerEventData e) { base.OnPointerEnter(e); pointerOver = true; tooltipSuppressed = false; RefreshNameTooltip(); }
    public override void OnPointerExit(PointerEventData e) { base.OnPointerExit(e); pointerOver = false; RefreshNameTooltip(); }
    public override void OnSelect(BaseEventData e) { base.OnSelect(e); keyboardFocused = !(e is PointerEventData); tooltipSuppressed = false; RefreshNameTooltip(); }
    public override void OnDeselect(BaseEventData e) { base.OnDeselect(e); keyboardFocused = false; RefreshNameTooltip(); }
    public override void OnPointerDown(PointerEventData e) { base.OnPointerDown(e); tooltipSuppressed = true; RefreshNameTooltip(); }
    public override void OnSubmit(BaseEventData e) { base.OnSubmit(e); tooltipSuppressed = true; RefreshNameTooltip(); }

    private void CaptureNormalAppearance()
    {
        if (!normalAppearanceCaptured && selectionLabel && targetGraphic) {
            normalBackgroundColor = targetGraphic.color;
            normalLabelColor = selectionLabel.color;
            normalAppearanceCaptured = true;
        }
    }

    public void SetPageSelected(bool selected)
    {
        pageSelectionManaged = true;
        pageSelected = selected;
        DoStateTransition(currentSelectionState, false);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        // Navigation can select inactive controls before Awake. Never restore an
        // uninitialized (transparent) colour, or recapture a selected tint later.
        if (Application.isPlaying) CaptureNormalAppearance();
        if (pageSelectionManaged && state != SelectionState.Disabled)
            state = pageSelected ? SelectionState.Selected
                : state == SelectionState.Selected ? SelectionState.Normal : state;
        base.DoStateTransition(state, instant);
        if (Application.isPlaying && selectionLabel && targetGraphic) {
            bool showSelected = state != SelectionState.Disabled &&
                (pageSelectionManaged ? pageSelected : state == SelectionState.Selected);
            targetGraphic.color = showSelected ? selectedBackgroundColor : normalBackgroundColor;
            selectionLabel.color = showSelected ? selectedLabelColor : normalLabelColor;
        }
    }
}
