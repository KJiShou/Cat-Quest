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
