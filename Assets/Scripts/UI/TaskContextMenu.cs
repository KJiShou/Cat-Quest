using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum TaskContextAction { Edit, Difficulty, Tags, Today, Tomorrow, ChooseDate, ClearDate, Complete, Delete }

public sealed class TaskContextChange
{
    public TaskContextAction field;
    public TaskDifficulty difficulty;
    public List<string> tagIds;
    public bool scheduled;
    public DateTime date;
}

// Owns only the popup draft. TaskSystemUI validates identity and persists through TaskService.
public sealed class TaskContextMenu : MonoBehaviour, IPointerClickHandler
{
    public RectTransform panel;
    public GameObject mainPanel, schedulePanel, difficultyPanel, tagsPanel, footer;
    public TMP_Text heading, clearDateLabel, status, emptyTags;
    public Button backdrop, edit, difficulty, tags, schedule, complete, delete, back, today, tomorrow, chooseDate, clearDate, save;
    public Button[] difficultyOptions;
    public TaskContextTagRow tagTemplate;
    public RectTransform tagContent;
    public ScrollRect tagScroll;
    public TaskDatePicker datePicker;
    public TaskDialogKeyboard keyboard, pickerKeyboard;
    Action<TaskContextAction> action;
    Func<TaskContextChange,string> apply;
    QuestTask original;
    DateTime localToday;
    TaskContextAction field;
    TaskDifficulty selectedDifficulty;
    readonly HashSet<string> selectedTags = new HashSet<string>();
    readonly List<TaskContextTagRow> tagRows = new List<TaskContextTagRow>();
    Vector2 screenPoint;
    GameObject returnFocus, lastTagFocus;
    Canvas canvas;
    RectTransform root;
    bool saving;
    float defaultPanelWidth;

    void Awake()
    {
        canvas = GetComponentInParent<Canvas>().rootCanvas; root = (RectTransform)transform;
        defaultPanelWidth=panel.sizeDelta.x;
        backdrop.onClick.AddListener(Dismiss); TaskContextTarget.Bind(backdrop.gameObject, _ => Dismiss());
        Bind(edit, TaskContextAction.Edit); Bind(complete, TaskContextAction.Complete); Bind(delete, TaskContextAction.Delete);
        difficulty.onClick.AddListener(() => ShowPanel(TaskContextAction.Difficulty));
        tags.onClick.AddListener(() => ShowPanel(TaskContextAction.Tags));
        schedule.onClick.AddListener(() => ShowPanel(TaskContextAction.ChooseDate));
        back.onClick.AddListener(() => ShowPanel(TaskContextAction.Edit));
        save.onClick.AddListener(Save);
        today.onClick.AddListener(() => datePicker.SelectDate(localToday));
        tomorrow.onClick.AddListener(() => datePicker.SelectDate(localToday.AddDays(1)));
        clearDate.onClick.AddListener(() => datePicker.noDate.onClick.Invoke());
        chooseDate.onClick.AddListener(() => {
            keyboard.enabled = false;
            pickerKeyboard.Open(new[] { datePicker.previousMonth,datePicker.nextMonth }.Concat(datePicker.days).Concat(new[] { datePicker.noDate }).ToArray(), CloseCalendar);
        });
        datePicker.DateSelected += _ => CalendarChanged();
        datePicker.DateCleared += CalendarChanged;
        datePicker.dismiss.onClick.AddListener(CloseCalendar);
        for (int i=0;i<difficultyOptions.Length;i++) {
            int index=i;
            difficultyOptions[i].onClick.AddListener(() => { selectedDifficulty=(TaskDifficulty)index; RefreshDifficulty(); ClearError(); });
        }
    }
    void Bind(Button button, TaskContextAction choice) => button.onClick.AddListener(() => {
        var callback=action; Dismiss(); callback?.Invoke(choice);
    });
    public void Show(QuestTask task, List<QuestTag> catalog, DateTime now, bool canComplete, Vector2 point,
        Action<TaskContextAction> callback, Func<TaskContextChange,string> onApply)
    {
        returnFocus=EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        original=PlayerDataStore.Copy(task); localToday=now.Date;
        action=callback; apply=onApply; screenPoint=point; heading.text=task.title;
        complete.interactable=canComplete; tomorrow.interactable=localToday<DateTime.MaxValue.Date;
        clearDateLabel.text=task.daily ? "Clear date & stop repeating" : "Clear date";
        datePicker.noDateLabel.text=clearDateLabel.text;
        foreach (var row in tagRows) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        tagRows.Clear();
        foreach (var tag in catalog) {
            var row=Instantiate(tagTemplate,tagContent); row.name="Tag_"+tag.id;
            row.Bind(tag,()=> {
                if (!selectedTags.Add(tag.id)) selectedTags.Remove(tag.id);
                row.SetSelected(selectedTags.Contains(tag.id)); ClearError();
            });
            row.gameObject.SetActive(true); tagRows.Add(row);
        }
        emptyTags.gameObject.SetActive(catalog.Count==0);
        tagScroll.gameObject.SetActive(catalog.Count>0);
        gameObject.SetActive(true); transform.SetAsLastSibling(); ShowPanel(TaskContextAction.Edit);
    }
    void ShowPanel(TaskContextAction choice)
    {
        field=choice;
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,choice==TaskContextAction.ChooseDate ? Mathf.Max(defaultPanelWidth,380) : defaultPanelWidth);
        save.GetComponentInChildren<TMP_Text>().text=choice==TaskContextAction.ChooseDate ? "Save schedule" : "Save";
        selectedDifficulty=original.difficulty;
        selectedTags.Clear();selectedTags.UnionWith(original.tagIds);
        datePicker.LoadValue(TaskService.TryDate(original.date,out var date) ? date : localToday,original.scheduled || original.daily);
        datePicker.overlay.SetActive(false);keyboard.enabled=true;
        mainPanel.SetActive(choice==TaskContextAction.Edit);
        difficultyPanel.SetActive(choice==TaskContextAction.Difficulty);
        tagsPanel.SetActive(choice==TaskContextAction.Tags);
        schedulePanel.SetActive(choice==TaskContextAction.ChooseDate);
        footer.SetActive(choice!=TaskContextAction.Edit);
        foreach (var row in tagRows) row.SetSelected(selectedTags.Contains(row.TagId));
        RefreshDifficulty(); ClearError(); FocusControls();
        tagScroll.verticalNormalizedPosition=1; lastTagFocus=null;
        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(panel); Position();
    }
    void FocusControls()
    {
        IEnumerable<Button> controls = field==TaskContextAction.Edit ? new[] { edit,difficulty,tags,schedule,complete,delete }
            : field==TaskContextAction.Difficulty ? difficultyOptions
            : field==TaskContextAction.Tags ? tagRows.Select(r=>r.button)
            : new[] { datePicker.previousMonth,datePicker.nextMonth }.Concat(datePicker.days).Concat(new[] { today,tomorrow,clearDate });
        if (field!=TaskContextAction.Edit) controls=controls.Concat(new[] { save,back });
        var input = Keyboard.current;
        bool keyboardInput = input != null && (input.contextMenuKey.isPressed || input.f10Key.isPressed ||
            input.enterKey.isPressed || input.numpadEnterKey.isPressed || input.spaceKey.isPressed);
        keyboard.Open(controls.Where(b=>b.gameObject.activeInHierarchy && b.IsInteractable()).ToArray(),Dismiss,keyboardInput);
    }
    void RefreshDifficulty()
    {
        for(int i=0;i<difficultyOptions.Length;i++) {
            bool selected=i==(int)selectedDifficulty;
            difficultyOptions[i].GetComponent<Image>().color=selected ? new Color32(97,113,164,255) : new Color32(231,239,250,255);
            difficultyOptions[i].GetComponentInChildren<TMP_Text>().color=selected ? Color.white : TaskUIBuilder.Ink;
        }
    }
    void ClearError()
    {
        if (!datePicker.HasDate) datePicker.dateLabel.text="No date";
        if (field==TaskContextAction.ChooseDate) {
            RefreshDateShortcut(today,localToday,"Today");
            RefreshDateShortcut(tomorrow,localToday<DateTime.MaxValue.Date ? localToday.AddDays(1) : localToday,"Tomorrow");
        }
        status.color=TaskUIBuilder.Ink;
        status.text=field==TaskContextAction.ChooseDate && original.daily
            ? datePicker.HasDate ? "Changes the repeat start date." : "Saving will also stop repetition."
            : "";
        status.gameObject.SetActive(status.text.Length>0);
    }
    void RefreshDateShortcut(Button button, DateTime date, string label)
    {
        bool selected=datePicker.HasDate && datePicker.SelectedDate==date;
        button.GetComponent<Image>().color=selected ? new Color32(97,113,164,255) : new Color32(231,239,250,255);
        var text=button.GetComponentInChildren<TMP_Text>();
        text.text=label;
        text.color=selected ? Color.white : TaskUIBuilder.Ink;
    }
    void CalendarChanged() { if (!datePicker.embedded) CloseCalendar(); ClearError(); }
    void CloseCalendar()
    {
        datePicker.overlay.SetActive(false);
        if (!gameObject.activeInHierarchy) return;
        keyboard.enabled=true; FocusControls(); chooseDate.Select();
    }
    void Save()
    {
        if (saving || apply==null || field==TaskContextAction.Edit) return;
        saving=true;
        try {
            var error=apply(new TaskContextChange { field=field,difficulty=selectedDifficulty,tagIds=selectedTags.ToList(),scheduled=datePicker.HasDate,date=datePicker.SelectedDate });
            if (error==null) Dismiss();
            else { status.text=error;status.color=new Color32(143,36,56,255);status.gameObject.SetActive(true); }
        } finally { saving=false; }
    }
    void Position()
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screenPoint,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var point);
        var bounds=root.rect;
        panel.localPosition=new Vector3(Mathf.Clamp(point.x,bounds.xMin+12,bounds.xMax-panel.rect.width-12),Mathf.Clamp(point.y,bounds.yMin+panel.rect.height+12,bounds.yMax-12),0);
    }
    void LateUpdate()
    {
        Position();
        var focused=EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (field!=TaskContextAction.Tags || !focused || focused==lastTagFocus || !focused.transform.IsChildOf(tagContent)) return;
        lastTagFocus=focused; Canvas.ForceUpdateCanvases();
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(tagScroll.viewport,focused.transform);
        var viewport=tagScroll.viewport.rect;
        float shift=bounds.min.y<viewport.yMin ? viewport.yMin-bounds.min.y : bounds.max.y>viewport.yMax ? viewport.yMax-bounds.max.y : 0;
        tagScroll.StopMovement(); tagContent.anchoredPosition+=new Vector2(0,shift);
    }
    public void Dismiss()
    {
        action=null;apply=null; gameObject.SetActive(false);
        if (EventSystem.current && returnFocus && returnFocus.activeInHierarchy) EventSystem.current.SetSelectedGameObject(returnFocus);
        returnFocus=null;
    }
    public void OnPointerClick(PointerEventData e) { if(e.button==PointerEventData.InputButton.Right) Dismiss(); }
    void OnApplicationFocus(bool focused) { if(!focused) Dismiss(); }
    void OnDisable() { action=null;apply=null;if(datePicker)datePicker.overlay.SetActive(false); }
}
