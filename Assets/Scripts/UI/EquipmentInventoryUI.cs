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
    public ScrollRect scroll;
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
    int visibleRows=4;
    GameObject lastSelection;
    public int Columns=>Mathf.Max(1,grid.constraintCount);
    public void SetVisibleItemCount(int count) => visibleRows=Mathf.Max(4,Mathf.CeilToInt(count/(float)Columns));

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
        // Stable squares and four visible rows; added equipment increases scroll extent.
        var viewport=scroll?scroll.viewport:backpack;
        float side=Mathf.Max(0,Mathf.Min(viewport.rect.width/(Columns+(Columns-1)*.15f),viewport.rect.height/4.45f));
        grid.cellSize=Vector2.one*side;
        grid.spacing=Vector2.one*(side*.15f);
        if(scroll){
            float height=Mathf.Max(viewport.rect.height,side*(visibleRows+(visibleRows-1)*.15f));
            if(Mathf.Abs(backpack.rect.height-height)>.1f)backpack.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
            var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(Application.IsPlaying(gameObject)&&selected&&selected!=lastSelection&&selected.activeInHierarchy&&selected.transform.IsChildOf(backpack)){
                var item=selected.GetComponentInParent<EquipmentInventoryItemUI>();
                var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(viewport,item?item.transform:selected.transform);
                float delta=bounds.min.y<viewport.rect.yMin?viewport.rect.yMin-bounds.min.y:bounds.max.y>viewport.rect.yMax?viewport.rect.yMax-bounds.max.y:0;
                if(delta!=0){scroll.StopMovement();var p=backpack.anchoredPosition;p.y=Mathf.Clamp(p.y+delta,0,Mathf.Max(0,height-viewport.rect.height));backpack.anchoredPosition=p;}
            }
            lastSelection=selected;
        }
    }
}
