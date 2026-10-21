using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Reuses the SelectionDropdown prefab's popup and keyboard navigation, while
// allowing each actual tag to toggle independently without closing the list.
[RequireComponent(typeof(TMP_Dropdown))]
public class TaskTagSelection : MonoBehaviour
{
    public event Action<int, bool> SelectionChanged;
    private readonly HashSet<int> selected = new HashSet<int>();
    private readonly List<Toggle> items = new List<Toggle>();
    private Transform configuredList;
    private readonly List<string> colors = new List<string>();
    private Sprite swatchSprite;

    public void SetColors(IEnumerable<string> values, Sprite sprite)
    {
        colors.Clear(); colors.AddRange(values);
        swatchSprite = sprite;
        configuredList = null;
    }

    public void SetSelection(IEnumerable<int> indices)
    {
        selected.Clear();
        foreach (int index in indices) selected.Add(index);
        for (int i = 0; i < items.Count; i++)
            if (items[i])
            {
                items[i].SetIsOnWithoutNotify(selected.Contains(i));
                if (items[i].graphic) items[i].graphic.CrossFadeAlpha(selected.Contains(i) ? 1 : 0, 0, true);
            }
    }

    private void LateUpdate()
    {
        var list = transform.Find("Dropdown List");
        if (!list || !list.gameObject.activeInHierarchy)
        {
            configuredList = null;
            items.Clear();
            return;
        }
        if (list == configuredList) return;
        configuredList = list;
        items.Clear();
        items.AddRange(list.GetComponentsInChildren<Toggle>(false));
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var toggle = items[i];
            if (i < colors.Count)
            {
                var swatch = toggle.transform.Find("TagColor") as RectTransform;
                if (!swatch)
                {
                    swatch = TaskUIBuilder.Rect(toggle.transform, "TagColor");
                    swatch.anchorMin = swatch.anchorMax = new Vector2(1, .5f);
                    swatch.anchoredPosition = new Vector2(-18, 0);
                    swatch.sizeDelta = new Vector2(16, 16);
                    var label = toggle.GetComponentInChildren<TMP_Text>();
                    if (label) label.rectTransform.offsetMax = new Vector2(-36, label.rectTransform.offsetMax.y);
                }
                var graphic = swatch.GetComponent<Image>() ?? swatch.gameObject.AddComponent<Image>();
                graphic.sprite = swatchSprite; graphic.type = Image.Type.Sliced;
                graphic.color = TagColors.Background(colors[i]); graphic.raycastTarget = false;
            }
            toggle.group = null;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(selected.Contains(index));
            if (toggle.graphic) toggle.graphic.CrossFadeAlpha(selected.Contains(index) ? 1 : 0, 0, true);
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn) selected.Add(index); else selected.Remove(index);
                SelectionChanged?.Invoke(index, isOn);
            });
        }
    }
}
