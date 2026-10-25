using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Draft selections survive switching panels; only Apply/Clear changes the task query.
public sealed class TaskFilterDialog : MonoBehaviour
{
    public Button difficultyTab, tagsTab, apply, clear, close, backdrop;
    public TMP_Text difficultyLabel, tagsLabel;
    public GameObject difficultyFocus, tagsFocus;
    public GameObject difficultyPanel, tagsPanel, emptyTags;
    public Toggle[] difficultyOptions; // Any, Easy, Normal, Hard.
    public RectTransform tagContent;
    public Toggle tagTemplate;
    public ScrollRect tagScroll;
    readonly HashSet<string> selectedTags = new HashSet<string>();
    TaskDifficulty? selectedDifficulty;
    Action<TaskDifficulty?, HashSet<string>> commit;
    GameObject returnSelection;

    void Awake()
    {
        difficultyTab.onClick.AddListener(() => SelectPanel(false));
        tagsTab.onClick.AddListener(() => SelectPanel(true));
        apply.onClick.AddListener(() => Commit(false));
        clear.onClick.AddListener(() => Commit(true));
        close.onClick.AddListener(Dismiss);
        backdrop.onClick.AddListener(Dismiss);
        for (int i = 0; i < difficultyOptions.Length; i++)
        {
            int index = i;
            difficultyOptions[i].onValueChanged.AddListener(on => {
                if (!on) return;
                selectedDifficulty = index == 0 ? (TaskDifficulty?)null : (TaskDifficulty)(index - 1);
                for (int j = 0; j < difficultyOptions.Length; j++)
                    difficultyOptions[j].SetIsOnWithoutNotify(j == index);
                UpdateLabels();
            });
        }
    }

    public void Show(TaskDifficulty? difficulty, IEnumerable<string> tags, List<QuestTag> catalog,
        Action<TaskDifficulty?, HashSet<string>> onApply)
    {
        returnSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        selectedDifficulty = difficulty;
        selectedTags.Clear(); selectedTags.UnionWith(tags);
        selectedTags.RemoveWhere(id => !catalog.Exists(t => t.id == id));
        commit = onApply;
        foreach (Transform child in tagContent)
            if (child != tagTemplate.transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (var tag in catalog)
        {
            var option = Instantiate(tagTemplate, tagContent);
            option.name = "Tag_" + tag.id;
            option.GetComponentInChildren<TMP_Text>(true).text = tag.name;
            option.transform.Find("TagColor").GetComponent<Image>().color = TagColors.Background(tag.colorHex);
            option.SetIsOnWithoutNotify(selectedTags.Contains(tag.id));
            option.onValueChanged.AddListener(on => {
                if (on) selectedTags.Add(tag.id); else selectedTags.Remove(tag.id);
                UpdateLabels();
            });
            option.gameObject.SetActive(true);
        }
        emptyTags.SetActive(catalog.Count == 0);
        for (int i = 0; i < difficultyOptions.Length; i++)
            difficultyOptions[i].SetIsOnWithoutNotify(i == (difficulty.HasValue ? (int)difficulty.Value + 1 : 0));
        gameObject.SetActive(true); transform.SetAsLastSibling();
        SelectPanel(false); UpdateLabels();
        tagScroll.verticalNormalizedPosition = 1;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(difficultyTab.gameObject);
    }

    void SelectPanel(bool tags)
    {
        difficultyPanel.SetActive(!tags); tagsPanel.SetActive(tags);
        difficultyFocus.SetActive(!tags); tagsFocus.SetActive(tags);
        difficultyLabel.color = tags ? new Color32(193,201,208,255) : new Color32(88,121,255,255);
        tagsLabel.color = tags ? new Color32(88,121,255,255) : new Color32(193,201,208,255);
    }
    void UpdateLabels()
    {
        difficultyLabel.text = selectedDifficulty.HasValue ? "Difficulty (1)" : "Difficulty";
        tagsLabel.text = selectedTags.Count > 0 ? "Tags (" + selectedTags.Count + ")" : "Tags";
    }
    void Commit(bool reset)
    {
        var action = commit;
        var difficulty = reset ? null : selectedDifficulty;
        var tags = reset ? new HashSet<string>() : new HashSet<string>(selectedTags);
        Dismiss(); action?.Invoke(difficulty, tags);
    }
    public void Dismiss()
    {
        commit = null;
        gameObject.SetActive(false);
        if (EventSystem.current && returnSelection && returnSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(returnSelection);
        returnSelection = null;
    }
    void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Dismiss(); }
}
