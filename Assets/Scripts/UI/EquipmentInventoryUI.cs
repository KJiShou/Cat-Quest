using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Editable SimpleCasual inventory surface; EquipmentPageUI owns actions and data.
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

    void LateUpdate()
    {
        // Six columns, four rows with the same spacing-to-cell ratio as the source.
        float side=Mathf.Max(0,Mathf.Min(backpack.rect.width/6.75f,backpack.rect.height/4.45f));
        grid.cellSize=Vector2.one*side;
        grid.spacing=Vector2.one*(side*.15f);
    }
}
