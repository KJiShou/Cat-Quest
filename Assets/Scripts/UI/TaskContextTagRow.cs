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
    public void Bind(QuestTag tag, Action toggle)
    {
        TagId=tag.id; label.text=tag.name;
        swatch.color=TagColors.Background(tag.colorHex);
        button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>toggle());
    }
    public void SetSelected(bool selected) => selectedMark.enabled=selected;
}
