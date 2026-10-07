using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// UI interactions only; task persistence belongs to the task data system.
public class EditTaskDetailsUI : MonoBehaviour
{
    public TMP_InputField taskTitle;
    public TMP_InputField description;
    public TMP_Text characterCount;
    public Button clearTitle;
    public Button addSubtask;
    public RectTransform subtaskContent;
    public GameObject subtaskTemplate;
    public TMP_Dropdown difficulty;
    public Image difficultyBadge;
    public Sprite[] difficultySprites;
    public TMP_Dropdown tagSelection;
    public TaskTagSelection tagMultiSelection;
    public RectTransform tagContent;
    public GameObject tagTemplate;
    public ScrollRect subtaskScroll;
    public ScrollRect formScroll;
    public LayoutElement formContentMinimum;
    public TaskTimePicker timePicker;

    public System.TimeSpan SelectedTime => timePicker.SelectedTime;
    private Coroutine pendingSubtaskScroll;

    private void LateUpdate()
    {
        float height = formScroll.viewport.rect.height;
        if (Mathf.Abs(formContentMinimum.minHeight - height) > .1f)
            formContentMinimum.minHeight = height;
    }

    private void Awake()
    {
        clearTitle.onClick.AddListener(() => taskTitle.text = string.Empty);
        description.onValueChanged.AddListener(UpdateCount);
        addSubtask.onClick.AddListener(AddSubtask);
        difficulty.onValueChanged.AddListener(UpdateDifficulty);
        UpdateDifficulty(difficulty.value);
        tagMultiSelection.SelectionChanged += SetTag;
        foreach (Transform chip in tagContent)
            if (chip.name == "TagChip") BindTag(chip.gameObject);
        foreach (Transform row in subtaskContent) BindRemove(row.gameObject);
        RefreshTags();
        UpdateCount(description.text);
    }

    private void UpdateCount(string value) => characterCount.text = value.Length + "/500";

    private void UpdateDifficulty(int value)
    {
        difficultyBadge.sprite = difficultySprites[value];
        difficulty.captionText.color = value == 2 ? new Color32(255, 235, 96, 255)
            : value == 1 ? new Color32(0, 118, 220, 255) : new Color32(0, 166, 85, 255);
    }

    private void AddSubtask()
    {
        var row = Instantiate(subtaskTemplate, subtaskContent);
        row.name = "Subtask";
        row.SetActive(true);
        row.GetComponentInChildren<Toggle>().isOn = false;
        var input = row.GetComponentInChildren<TMP_InputField>();
        input.text = string.Empty;
        BindRemove(row);
        input.Select();
        input.ActivateInputField();
        if (pendingSubtaskScroll != null) StopCoroutine(pendingSubtaskScroll);
        pendingSubtaskScroll = StartCoroutine(ScrollToNewSubtask());
    }

    private IEnumerator ScrollToNewSubtask()
    {
        yield return null;
        formContentMinimum.minHeight = formScroll.viewport.rect.height;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
        formScroll.StopMovement();
        formScroll.verticalNormalizedPosition = 0;
        subtaskScroll.StopMovement();
        subtaskScroll.verticalNormalizedPosition = 0;
        yield return null;
        Canvas.ForceUpdateCanvases();
        formScroll.verticalNormalizedPosition = 0;
        subtaskScroll.verticalNormalizedPosition = 0;
        pendingSubtaskScroll = null;
    }

    private void BindRemove(GameObject row)
    {
        var toggle = row.GetComponentInChildren<Toggle>();
        var tick = toggle.transform.Find("On/On (1)").GetComponent<Image>();
        tick.enabled = toggle.isOn;
        toggle.onValueChanged.AddListener(value => tick.enabled = value);
        var remove = row.transform.Find("Remove").GetComponent<Button>();
        remove.onClick.RemoveAllListeners();
        remove.onClick.AddListener(() => { row.SetActive(false); Destroy(row); });
    }

    private void SetTag(int value, bool isSelected)
    {
        var label = tagSelection.options[value].text + " ×";
        foreach (Transform existing in tagContent)
            if (existing.gameObject.activeSelf && existing.GetComponentInChildren<TMP_Text>().text == label)
            {
                if (!isSelected) { existing.gameObject.SetActive(false); Destroy(existing.gameObject); }
                RefreshTags();
                return;
            }
        if (!isSelected) return;
        var chip = Instantiate(tagTemplate, tagContent);
        chip.name = "TagChip";
        chip.SetActive(true);
        chip.GetComponentInChildren<TMP_Text>().text = label;
        SizeTag(chip);
        BindTag(chip);
        RefreshTags();
    }

    private void BindTag(GameObject chip)
    {
        var button = chip.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => { chip.SetActive(false); Destroy(chip); RefreshTags(); });
    }

    private static void SizeTag(GameObject chip)
    {
        chip.GetComponent<LayoutElement>().preferredWidth = Mathf.Ceil(chip.GetComponentInChildren<TMP_Text>().GetPreferredValues().x) + 24;
    }

    private void RefreshTags()
    {
        bool hasTags = false;
        var selected = new List<int>();
        foreach (Transform chip in tagContent)
            if (chip.gameObject.activeSelf)
            {
                hasTags = true;
                SizeTag(chip.gameObject);
                var text = chip.GetComponentInChildren<TMP_Text>().text;
                for (int i = 0; i < tagSelection.options.Count; i++)
                    if (text == tagSelection.options[i].text + " ×") selected.Add(i);
            }
        tagMultiSelection.SetSelection(selected);
        tagSelection.captionText.gameObject.SetActive(!hasTags);
        tagSelection.captionText.text = "Add tags...";
        LayoutRebuilder.MarkLayoutForRebuild(tagContent);
    }

    private void OnDestroy()
    {
        if (tagMultiSelection) tagMultiSelection.SelectionChanged -= SetTag;
    }
}
