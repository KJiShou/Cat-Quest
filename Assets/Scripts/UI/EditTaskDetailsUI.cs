using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Owns an isolated editing draft. The controller commits it through TaskService.
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
    public Sprite tagTintSprite;
    public ScrollRect subtaskScroll;
    public ScrollRect formScroll;
    public LayoutElement formContentMinimum;
    // Retained binding to the inactive authored control; schedules now use dates only.
    public TaskTimePicker timePicker;
    public TaskDatePicker datePicker;
    public TMP_Dropdown repeatChoice, repeatUnit;
    public TMP_InputField repeatInterval, repeatUntil;
    public TMP_Dropdown repeatDays;
    public GameObject customRepeatEndRow;
    public GameObject customRepeatRow;
    public Toggle dailyToggle;
    private string editingId;
    private bool initialized;
    private readonly Dictionary<GameObject, string> rowIds = new Dictionary<GameObject, string>();
    private readonly Dictionary<GameObject, string> chipIds = new Dictionary<GameObject, string>();
    private readonly HashSet<string> selectedTagIds = new HashSet<string>();
    private List<QuestTag> tagCatalog = new List<QuestTag>();

    private string weekdayLabel;
    private int legacyMinuteOfDay;
    private Coroutine pendingSubtaskScroll;
    private LayoutElement tagSelectionSize;
    private TMP_InputField submittedSubtask;

    private void LateUpdate()
    {
        if (repeatDays.gameObject.activeInHierarchy && weekdayLabel != null && repeatDays.captionText.text != weekdayLabel)
            repeatDays.captionText.text = weekdayLabel;
        if (submittedSubtask)
        {
            var submitted = submittedSubtask; submittedSubtask = null;
            AdvanceSubtask(submitted);
        }
        float height = formScroll.viewport.rect.height;
        if (Mathf.Abs(formContentMinimum.minHeight - height) > .1f)
            formContentMinimum.minHeight = height;
        if (tagSelection.captionText.gameObject.activeSelf && tagSelection.captionText.text != "Add tags...")
            tagSelection.captionText.text = "Add tags...";
        float tagHeight = Mathf.Max(48, LayoutUtility.GetPreferredHeight(tagContent) + 16);
        if (Mathf.Abs(tagSelectionSize.preferredHeight - tagHeight) > .1f)
            tagSelectionSize.preferredHeight = tagHeight;
    }

    private void Awake() => Initialize();
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        tagSelectionSize = TaskUIBuilder.Height((RectTransform)tagContent.parent,48);
        tagContent.parent.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
        foreach (var input in GetComponentsInChildren<TMP_InputField>(true)) TaskUIBuilder.BlueCaret(input);
        clearTitle.onClick.AddListener(() => taskTitle.text = string.Empty);
        description.onValueChanged.AddListener(UpdateCount);
        addSubtask.onClick.AddListener(AddSubtask);
        difficulty.options[1].text = "Medium";
        difficulty.onValueChanged.AddListener(UpdateDifficulty);
        UpdateDifficulty(difficulty.value);
        tagMultiSelection.SelectionChanged += SetTag;
        foreach (Transform chip in tagContent)
            if (chip.name == "TagChip") BindTag(chip.gameObject);
        foreach (Transform row in subtaskContent) BindRemove(row.gameObject);
        RefreshTags();
        UpdateCount(description.text);
        dailyToggle.onValueChanged.AddListener(enabled => {
            repeatChoice.SetValueWithoutNotify(enabled ? 1 : 0);
            UpdateSchedule();
        });
        repeatChoice.onValueChanged.AddListener(choice => {
            dailyToggle.SetIsOnWithoutNotify(choice != 0);
            UpdateSchedule();
        });
        repeatDays.gameObject.AddComponent<TaskTagSelection>().BindMask(repeatDays);
        repeatDays.onValueChanged.AddListener(UpdateWeekdayLabel);
        repeatUnit.onValueChanged.AddListener(_ => UpdateSchedule());
        datePicker.DateSelected += UpdateRepeatLabels;
        datePicker.DateCleared += () =>
        {
            repeatChoice.SetValueWithoutNotify(0);
            dailyToggle.SetIsOnWithoutNotify(false);
            UpdateSchedule();
        };
    }

    private void UpdateSchedule()
    {
        customRepeatRow.SetActive(repeatChoice.value == 6);
        customRepeatEndRow.SetActive(customRepeatRow.activeSelf);
        repeatDays.gameObject.SetActive(customRepeatRow.activeSelf && repeatUnit.value == (int)RepeatUnit.Weeks);
        if (dailyToggle.isOn && !datePicker.HasDate) datePicker.LoadValue(datePicker.SelectedDate);
        datePicker.noDateLabel.text = dailyToggle.isOn ? "No date · Turn off repeat" : "No date";
    }

    static readonly RepeatUnit[] Presets = { RepeatUnit.Days, RepeatUnit.Days, RepeatUnit.Weeks,
        RepeatUnit.MonthlyWeekday, RepeatUnit.Years, RepeatUnit.Weekdays };

    void UpdateWeekdayLabel(int mask)
    {
        var names = new List<string>();
        for (int day = 0; day < 7; day++)
            if ((mask & (1 << day)) != 0) names.Add(repeatDays.options[day].text);
        weekdayLabel = names.Count == 0 ? "Select days" : names.Count == 7 ? "All days" : string.Join(", ", names);
    }

    void UpdateRepeatLabels(System.DateTime start)
    {
        var labels = new List<string> { "Does not repeat" };
        for (int i = 1; i < Presets.Length; i++) labels.Add(TaskRepeat.Label(Presets[i], 1, start));
        labels.Add("Custom repeat...");
        int selected = repeatChoice.value;
        repeatChoice.ClearOptions();
        repeatChoice.AddOptions(labels);
        repeatChoice.SetValueWithoutNotify(selected);
        repeatChoice.RefreshShownValue();
    }

    public void LoadDraft(QuestTask task, List<QuestTag> tags, IEnumerable<string> completed)
    {
        Initialize(); submittedSubtask = null; editingId = task.id;
        selectedTagIds.Clear();
        selectedTagIds.UnionWith(task.tagIds);
        taskTitle.SetTextWithoutNotify(task.title ?? ""); description.SetTextWithoutNotify(task.description ?? ""); UpdateCount(description.text);
        difficulty.SetValueWithoutNotify((int)task.difficulty); UpdateDifficulty((int)task.difficulty);
        dailyToggle.SetIsOnWithoutNotify(task.daily);
        datePicker.LoadValue(TaskService.TryDate(task.date, out var date) ? date : System.DateTime.Today, task.scheduled || task.daily);
        UpdateRepeatLabels(datePicker.SelectedDate);
        int preset = System.Array.IndexOf(Presets, task.repeatUnit, 1);
        repeatChoice.SetValueWithoutNotify(!task.daily ? 0 : task.repeatInterval > 1 || task.repeatWeekdays != 0 || !string.IsNullOrEmpty(task.repeatUntil) || preset < 0 ? 6 : preset);
        repeatUnit.SetValueWithoutNotify((int)task.repeatUnit < 4 ? (int)task.repeatUnit : 0);
        repeatInterval.SetTextWithoutNotify(System.Math.Max(1, task.repeatInterval).ToString());
        repeatDays.SetValueWithoutNotify(task.repeatWeekdays != 0 ? task.repeatWeekdays : 1 << (((int)datePicker.SelectedDate.DayOfWeek + 6) % 7));
        UpdateWeekdayLabel(repeatDays.value);
        repeatUntil.SetTextWithoutNotify(task.repeatUntil ?? "");
        legacyMinuteOfDay = task.minuteOfDay;
        UpdateSchedule();
        LoadTagCatalog(tags);
        ClearChildren(subtaskContent); rowIds.Clear();
        var done = new HashSet<string>(completed ?? System.Array.Empty<string>());
        foreach (var subtask in task.subtasks) CreateSubtask(subtask.id, subtask.title, done.Contains(subtask.id));
        formScroll.verticalNormalizedPosition = 1; subtaskScroll.verticalNormalizedPosition = 1;
    }

    public QuestTask ReadDraft(out List<string> completed)
    {
        completed = new List<string>();
        var task = new QuestTask { id = editingId, title = taskTitle.text, description = description.text,
            difficulty = (TaskDifficulty)difficulty.value, daily = dailyToggle.isOn, scheduled = datePicker.HasDate,
            date = TaskService.DateKey(datePicker.SelectedDate), minuteOfDay = legacyMinuteOfDay };
        if (task.daily)
        {
            bool custom = repeatChoice.value == 6;
            task.repeatUnit = custom ? (RepeatUnit)repeatUnit.value : Presets[repeatChoice.value];
            if (custom && task.repeatUnit == RepeatUnit.Weeks)
                task.repeatWeekdays = repeatDays.value == 0 ? -1 : repeatDays.value;
            if (custom) task.repeatUntil = repeatUntil.text.Trim();
            task.repeatInterval = custom ? (int.TryParse(repeatInterval.text, out int n) && n > 0 ? n : -1) : 1;
        }
        foreach (Transform row in subtaskContent)
        {
            if (!row.gameObject.activeSelf) continue;
            string title = row.GetComponentInChildren<TMP_InputField>().text.Trim();
            if (title.Length == 0) continue;
            if (!rowIds.TryGetValue(row.gameObject, out var id)) rowIds[row.gameObject] = id = System.Guid.NewGuid().ToString("N");
            task.subtasks.Add(new QuestSubtask { id = id, title = title });
            if (row.GetComponentInChildren<Toggle>().isOn) completed.Add(id);
        }
        foreach (var tag in tagCatalog)
            if (selectedTagIds.Contains(tag.id)) task.tagIds.Add(tag.id);
        return task;
    }

    public void RefreshCatalog(List<QuestTag> tags)
    {
        if(tagCatalog.Count==tags.Count && tagCatalog.TrueForAll(old=>tags.Exists(tag=>tag.id==old.id && tag.name==old.name && tag.colorHex==old.colorHex))) return;
        // A catalog edit must not rebuild the form or lose blank unsaved subtasks/scroll position.
        LoadTagCatalog(tags);
    }
    private void LoadTagCatalog(List<QuestTag> tags)
    {
        tagCatalog = tags.ConvertAll(tag => new QuestTag { id = tag.id, name = tag.name, colorHex = tag.colorHex });
        selectedTagIds.RemoveWhere(id => !tagCatalog.Exists(tag => tag.id == id));
        if (tagSelection.IsExpanded) tagSelection.Hide();
        ClearChildren(tagContent); chipIds.Clear();
        tagSelection.ClearOptions(); tagSelection.AddOptions(tagCatalog.ConvertAll(tag => tag.name));
        tagMultiSelection.SetColors(tagCatalog.ConvertAll(tag => tag.colorHex), tagTintSprite);
        foreach (var tag in tagCatalog)
            if (selectedTagIds.Contains(tag.id)) CreateTagChip(tag);
        RefreshTags();
    }
    private static void ClearChildren(Transform parent)
    {
        foreach (Transform child in parent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
    }

    private void UpdateCount(string value) => characterCount.text = value.Length + "/500";

    private void UpdateDifficulty(int value)
    {
        difficultyBadge.sprite = difficultySprites[value];
        difficultyBadge.type = Image.Type.Simple;
        difficultyBadge.preserveAspect = true;
        difficulty.captionText.text = TaskService.DifficultyName((TaskDifficulty)value);
        difficulty.captionText.color = TaskUIBuilder.DifficultyColor((TaskDifficulty)value);
        difficulty.captionText.alignment = TextAlignmentOptions.Center;
    }

    private void AddSubtask()
    {
        var row = CreateSubtask(System.Guid.NewGuid().ToString("N"), "", false);
        var input = row.GetComponentInChildren<TMP_InputField>();
        input.Select(); input.ActivateInputField();
        if (pendingSubtaskScroll != null) StopCoroutine(pendingSubtaskScroll);
        pendingSubtaskScroll = StartCoroutine(ScrollToNewSubtask());
    }
    private GameObject CreateSubtask(string id, string title, bool done)
    {
        var row = Instantiate(subtaskTemplate, subtaskContent);
        row.name = "Subtask";
        row.SetActive(true);
        rowIds[row] = id;
        row.GetComponentInChildren<Toggle>().SetIsOnWithoutNotify(done);
        var input = row.GetComponentInChildren<TMP_InputField>();
        TaskUIBuilder.BlueCaret(input);
        input.SetTextWithoutNotify(title);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onSubmit.AddListener(_ => submittedSubtask = input);
        BindRemove(row);
        return row;
    }

    private void AdvanceSubtask(TMP_InputField input)
    {
        if (!input.gameObject.activeInHierarchy || input.wasCanceled) return;
        var rows = subtaskContent.GetComponentsInChildren<TMP_InputField>();
        int index = System.Array.IndexOf(rows, input);
        if (index < 0) return;
        if (index + 1 < rows.Length) FocusSubtask(rows[index + 1]);
        else if (!string.IsNullOrWhiteSpace(input.text)) AddSubtask();
        else FocusSubtask(input);
    }
    private void FocusSubtask(TMP_InputField input)
    {
        input.Select(); input.ActivateInputField();
        Canvas.ForceUpdateCanvases();
        formScroll.StopMovement(); formScroll.verticalNormalizedPosition = 0;
        subtaskScroll.StopMovement();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(subtaskScroll.viewport, input.transform);
        float shift = bounds.min.y < subtaskScroll.viewport.rect.yMin ? subtaskScroll.viewport.rect.yMin - bounds.min.y
            : bounds.max.y > subtaskScroll.viewport.rect.yMax ? subtaskScroll.viewport.rect.yMax - bounds.max.y : 0;
        var position = subtaskContent.anchoredPosition;
        position.y = Mathf.Clamp(position.y + shift, 0, Mathf.Max(0, subtaskContent.rect.height - subtaskScroll.viewport.rect.height));
        subtaskContent.anchoredPosition = position;
    }
    private void OnDisable()
    {
        submittedSubtask = null;
        if (pendingSubtaskScroll != null) StopCoroutine(pendingSubtaskScroll);
        pendingSubtaskScroll = null;
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
        if (value < 0 || value >= tagCatalog.Count) return;
        var tag = tagCatalog[value];
        if (isSelected)
        {
            if (selectedTagIds.Add(tag.id)) CreateTagChip(tag);
        }
        else
        {
            selectedTagIds.Remove(tag.id);
            foreach (Transform existing in tagContent)
                if (chipIds.TryGetValue(existing.gameObject, out var id) && id == tag.id)
                {
                    chipIds.Remove(existing.gameObject);
                    existing.gameObject.SetActive(false); Destroy(existing.gameObject);
                }
        }
        RefreshTags();
    }
    private void CreateTagChip(QuestTag tag)
    {
        var chip = Instantiate(tagTemplate, tagContent);
        chip.name = "TagChip";
        chip.SetActive(true);
        chipIds[chip] = tag.id;
        chip.GetComponentInChildren<TMP_Text>().text = tag.name + " Ã—";
        TagColors.Apply(chip, tag.colorHex, tagTintSprite);
        SizeTag(chip);
        BindTag(chip);
    }

    private void BindTag(GameObject chip)
    {
        var button = chip.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            if (chipIds.TryGetValue(chip, out var id)) selectedTagIds.Remove(id);
            chipIds.Remove(chip);
            chip.SetActive(false); Destroy(chip); RefreshTags();
        });
    }

    private static void SizeTag(GameObject chip)
    {
        var label = chip.GetComponentInChildren<TMP_Text>();
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var size = chip.GetComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = Mathf.Min(300, Mathf.Ceil(label.GetPreferredValues().x) + 24);
    }

    private void RefreshTags()
    {
        var selected = new List<int>();
        foreach (Transform chip in tagContent)
            if (chip.gameObject.activeSelf)
            {
                SizeTag(chip.gameObject);
            }
        for (int i = 0; i < tagCatalog.Count; i++)
            if (selectedTagIds.Contains(tagCatalog[i].id)) selected.Add(i);
        tagMultiSelection.SetSelection(selected);
        tagSelection.captionText.gameObject.SetActive(selected.Count == 0);
        tagSelection.captionText.text = "Add tags...";
        LayoutRebuilder.MarkLayoutForRebuild(tagContent);
    }

    private void OnDestroy()
    {
        if (tagMultiSelection) tagMultiSelection.SelectionChanged -= SetTag;
    }
}
