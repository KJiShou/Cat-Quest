using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Editable SimpleCasual inventory surface; EquipmentPageUI owns actions and data.
[ExecuteAlways]
public sealed class EquipmentInventoryUI : MonoBehaviour
{
    public RectTransform backpack;
    public GridLayoutGroup grid;
    public Button[] filters;
    public Image[] filterIcons;
    public GameObject[] filterFocus;
    public Button autoSelect;
    public TMP_Text notice;
    public EquipmentInventoryItemUI itemTemplate;
    public GameObject emptyTemplate;
    public Sprite[] tierBackgrounds;
    public GameObject emptyState;
    public TMP_Text emptyHeading, emptyMessage, emptyActionLabel;
    public Button emptyAction;
    public Image emptyIcon;
    public Sprite[] emptyStateIcons;
    public GameObject autoSelectFeedback, autoSelectRuleHint;
    public TMP_Text autoSelectPowerDelta;

    public void ShowAutoSelectRule(BaseEventData eventData)
    {
        if (isActiveAndEnabled && autoSelect && autoSelect.gameObject.activeInHierarchy && autoSelectRuleHint)
            autoSelectRuleHint.SetActive(true);
    }
    public void HideAutoSelectRule(BaseEventData eventData)
    {
        if (autoSelectRuleHint) autoSelectRuleHint.SetActive(false);
    }
    void OnDisable() => HideAutoSelectRule(null);
    void OnApplicationFocus(bool focused) { if (!focused) HideAutoSelectRule(null); }
    void Update()
    {
        if (Application.IsPlaying(gameObject) && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            HideAutoSelectRule(null);
    }

    void LateUpdate()
    {
        // Six columns, four rows with the same spacing-to-cell ratio as the source.
        float side=Mathf.Max(0,Mathf.Min(backpack.rect.width/6.75f,backpack.rect.height/4.45f));
        grid.cellSize=Vector2.one*side;
        grid.spacing=Vector2.one*(side*.15f);
    }
}
