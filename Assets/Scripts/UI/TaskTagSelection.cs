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
    private TMP_Dropdown maskDropdown;

    // TMP adds None and All before the individual options in a multi-select popup.
    public void BindMask(TMP_Dropdown dropdown) => maskDropdown = dropdown;

    private void RefreshMask()
    {
        int all = (1 << maskDropdown.options.Count) - 1;
        int mask = maskDropdown.value;
        for (int i = 0; i < items.Count; i++)
        {
            bool on = i == 0 ? mask == 0 : i == 1 ? mask == all : (mask & (1 << (i - 2))) != 0;
            items[i].SetIsOnWithoutNotify(on);
            if (items[i].graphic) items[i].graphic.CrossFadeAlpha(on ? 1 : 0, 0, true);
        }
    }

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
                    var label = toggle.GetComponentInChildren<TMP_Text>();
                    if (label)
                    {
                        // Keep the option's name inside its colored chip, as in the
                        // selected tags, while the outer toggle owns selection.
                        swatch.SetSiblingIndex(label.transform.GetSiblingIndex());
                        label.transform.SetParent(swatch, false);
                        TaskUIBuilder.LayoutTag(swatch.gameObject, label.text, label.fontSize, 30);
                        swatch.GetComponent<LayoutElement>().minWidth = 0;
                        label.overflowMode = TextOverflowModes.Ellipsis;
                        label.raycastTarget = false;
                    }
                }
                var graphic = swatch.GetComponent<Image>() ?? swatch.gameObject.AddComponent<Image>();
                graphic.sprite = swatchSprite; graphic.type = Image.Type.Sliced;
                graphic.raycastTarget = false;
                TagColors.Apply(swatch.gameObject, colors[i], swatchSprite);
            }
            toggle.group = null;
            toggle.onValueChanged.RemoveAllListeners();
            if (maskDropdown)
            {
                toggle.onValueChanged.AddListener(on =>
                {
                    int bit = index >= 2 ? 1 << (index - 2) : 0;
                    if (index == 0) maskDropdown.value = 0;
                    else if (index == 1) maskDropdown.value = (1 << maskDropdown.options.Count) - 1;
                    else maskDropdown.value = on ? maskDropdown.value | bit : maskDropdown.value & ~bit;
                    RefreshMask();
                });
                continue;
            }
            toggle.SetIsOnWithoutNotify(selected.Contains(index));
            if (toggle.graphic) toggle.graphic.CrossFadeAlpha(selected.Contains(index) ? 1 : 0, 0, true);
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn) selected.Add(index); else selected.Remove(index);
                SelectionChanged?.Invoke(index, isOn);
            });
        }
        if (maskDropdown) RefreshMask();
    }
}
