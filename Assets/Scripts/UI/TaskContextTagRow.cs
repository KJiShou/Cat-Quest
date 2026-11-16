using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Reusable menu adaptation of the existing tag-filter option.
public sealed class TaskContextTagRow : MonoBehaviour
{
    public Button button;
    public Image selectedMark, swatch;
    public TMP_Text label;
    public string TagId { get; private set; }
    bool layoutPending;
    public void Bind(QuestTag tag, Action toggle)
    {
        TagId=tag.id; label.text=tag.name;
        layoutPending=true;
        TagColors.Apply(swatch.gameObject,tag.colorHex,swatch.sprite);
        button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>toggle());
    }
    void OnEnable() { layoutPending=true; }
    void LateUpdate()
    {
        if(!layoutPending) return;
        layoutPending=false;
        // Rows are bound while the Tags panel is inactive. Measure after TMP initializes.
        label.ForceMeshUpdate();
        TaskUIBuilder.LayoutTag(swatch.gameObject,label.text,18,34);
        var size=swatch.GetComponent<LayoutElement>();
        size.minWidth=0; size.preferredWidth=Mathf.Min(size.preferredWidth,240);
        label.overflowMode=TextOverflowModes.Ellipsis;
    }
    public void SetSelected(bool selected) => selectedMark.enabled=selected;
}
