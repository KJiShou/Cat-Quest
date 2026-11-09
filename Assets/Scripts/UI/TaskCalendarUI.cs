using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Presentation only. All task writes go through TaskSystemUI and its existing services.
public sealed class TaskCalendarUI : MonoBehaviour
{
    public Button listMode, calendarMode, previous, next, today, monthButton, add, filter, clear, noDateButton;
    public Sprite selectedViewSprite, neutralButtonSprite, todayButtonSprite;
    public TMP_Text monthLabel, noDateLabel, filterLabel;
    public TMP_Text noDateCount;
    public TMP_InputField search;
    public Toggle showCompleted;
    public GameObject listHeader, listBody;
    public RectTransform monthPanel, grid, drawer, drawerBody;
    public GridLayoutGroup gridLayout;
    public CanvasGroup monthInteraction;
    public Button backdrop, back, close;
    public TMP_Text drawerTitle, emptyLabel;
    public RectTransform rows, detailRoot;
    public ScrollRect rowScroll;
    public GameObject rowTemplate;
    public Button undo, drawerAdd;
    public TaskCalendarDayUI[] days;
    public TaskDatePicker jumpPicker;
    public TaskDisplayMode Mode { get; private set; }
    public bool Active => Mode == TaskDisplayMode.Calendar;
    public DateTime SelectedDate { get; private set; }
    public DateTime Month { get; private set; }
    public TaskService Service => owner.Service;
    public bool DrawerOpen => drawer.gameObject.activeSelf;
    TaskSystemUI owner;
    TaskDetailsPresenter details;
    TaskCalendarResult result;
    TaskListViewport drawerRows;
    DateTime boundMonth, boundToday;
    int boundVersion = -1, boundCellHeight = -1;
    TaskDifficulty? boundDifficulty;
    string boundSearch;
    bool boundCompleted;
    readonly HashSet<string> boundTags = new HashSet<string>();
    TaskDifficulty? difficulty;
    readonly HashSet<string> tags = new HashSet<string>();
    CalendarTaskItem selected;
    bool noDate, initialized, refreshing;
    int cellHeight = -1;
    Vector2 lastSize;
    float savedScroll = 1;
    RectTransform editRect;
    Transform editParent;
    int editIndex;
    Vector2 editMin, editMax, editPivot, editPosition, editSize;
    GameObject returnFocus;
    bool returningFromModal;
    string editDay;

    public void Initialize(TaskSystemUI system)
    {
        if (initialized) return;
        initialized = true; owner = system;
        SelectedDate = Service.LocalNow.Date; Month = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        details = new TaskDetailsPresenter(detailRoot, new TaskUIBuilder(owner.font, owner.roundedSprite), owner.tagTintSprite);
        listMode.onClick.AddListener(() => SetMode(TaskDisplayMode.List));
        calendarMode.onClick.AddListener(() => SetMode(TaskDisplayMode.Calendar));
        previous.onClick.AddListener(() => ChangeMonth(-1)); next.onClick.AddListener(() => ChangeMonth(1));
        today.onClick.AddListener(() => SelectDate(Service.LocalNow.Date, false, false));
        jumpPicker.DateSelected += date => SelectDate(date, false, false);
        add.onClick.AddListener(AddTask); drawerAdd.onClick.AddListener(AddTask);
        noDateButton.onClick.AddListener(OpenNoDate);
        filter.onClick.AddListener(() => owner.ShowCalendarFilters(difficulty, tags, (d, t) => { difficulty = d; tags.Clear(); tags.UnionWith(t); Refresh(); }));
        clear.onClick.AddListener(ClearFilters);
        search.onValueChanged.AddListener(_ => Refresh()); showCompleted.onValueChanged.AddListener(_ => Refresh());
        back.onClick.AddListener(Back); close.onClick.AddListener(CloseDrawer);
        backdrop.onClick.AddListener(() => { if (!owner.IsEditing) CloseDrawer(); });
        undo.onClick.AddListener(() => { if (selected?.view.history != null) owner.CalendarUndo(selected.view.history.id); });
        drawer.gameObject.SetActive(false); detailRoot.gameObject.SetActive(false);
        SetMode(TaskDisplayMode.List);
    }
    public void SetMode(TaskDisplayMode mode)
    {
        if (!initialized || owner.IsEditing || owner.HasTaskModal || !owner.CommitQuickAdd()) return;
        owner.CancelDraft(); owner.detailsRoot.gameObject.SetActive(false);
        Mode = mode;
        listHeader.SetActive(!Active); listBody.SetActive(!Active); gameObject.SetActive(Active);
        StyleViewButton(listMode, !Active);
        StyleViewButton(calendarMode, Active);
        if (Active) Refresh(); else owner.Refresh();
    }
    void StyleViewButton(Button button, bool selectedView)
    {
        button.image.sprite = selectedView ? selectedViewSprite : neutralButtonSprite;
        button.image.color = selectedView ? Color.white : new Color32(231,239,250,255);
        button.GetComponentInChildren<TMP_Text>().color = selectedView ? Color.white : TaskUIBuilder.Ink;
        button.transform.Find("TabFocus").gameObject.SetActive(false);
    }
    public void Refresh()
    {
        if (!initialized || !Active || refreshing) return;
        refreshing = true;
        try
        {
            var catalog = Service.TagCatalog;
            tags.RemoveWhere(id => !catalog.Any(t => t.id == id));
            int leading = ((int)Month.DayOfWeek + 6) % 7;
            var first = Month.AddDays(-Math.Min(leading, (Month - DateTime.MinValue).Days));
            var last = first.AddDays(Math.Min(41, (DateTime.MaxValue.Date - first).Days));
            var localDay = Service.LocalNow.Date;
            bool changed = result == null || boundVersion != Service.ReadVersion || boundToday != localDay ||
                boundMonth != Month || boundDifficulty != difficulty || boundSearch != search.text ||
                boundCompleted != showCompleted.isOn || !boundTags.SetEquals(tags);
            if (changed) {
                result = Service.QueryCalendar(first, last, search.text, difficulty, tags, showCompleted.isOn);
                boundVersion = Service.ReadVersion; boundToday = localDay; boundMonth = Month;
                boundDifficulty = difficulty; boundSearch = search.text; boundCompleted = showCompleted.isOn;
                boundTags.Clear(); boundTags.UnionWith(tags);
            }
            jumpPicker.LoadValue(SelectedDate);
            monthLabel.text = Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            bool isToday = SelectedDate == Service.LocalNow.Date;
            today.image.sprite = isToday ? todayButtonSprite : neutralButtonSprite;
            today.image.color = isToday ? Color.white : new Color32(220,239,255,255);
            previous.interactable = Month.Year > 1 || Month.Month > 1;
            next.interactable = Month.Year < 9999 || Month.Month < 12;
            noDateCount.text = result.noDate.Count.ToString();
            float badgeWidth = Mathf.Max(26, noDateCount.GetPreferredValues(noDateCount.text).x + 12);
            var badge = (RectTransform)noDateCount.transform.parent;
            badge.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, badgeWidth);
            noDateLabel.rectTransform.offsetMax = new Vector2(-badgeWidth - 16, noDateLabel.rectTransform.offsetMax.y);
            noDateButton.GetComponent<LayoutElement>().preferredWidth = Mathf.Max(155, noDateLabel.preferredWidth + badgeWidth + 32);
            filterLabel.text = "Filter" + (difficulty.HasValue || tags.Count > 0 ? " · On" : "");
            clear.gameObject.SetActive(HasFilters);
            if (changed || boundCellHeight != cellHeight) {
                boundCellHeight = cellHeight;
                int i = 0;
                foreach (var day in result.days.Values) {
                    days[i].gameObject.SetActive(true); days[i++].Bind(this, day.date, day, Month.Month, cellHeight);
                }
                for (; i < days.Length; i++) days[i].gameObject.SetActive(false);
            } else {
                foreach (var cell in days)
                    if (cell.gameObject.activeSelf) cell.selection.gameObject.SetActive(cell.Date == SelectedDate);
            }
            if (DrawerOpen && !owner.IsEditing) RenderDrawer();
            if (owner.IsEditing) owner.editor.RefreshCatalog(Service.TagCatalog);
        }
        finally { refreshing = false; }
    }
    bool HasFilters => !string.IsNullOrWhiteSpace(search.text) || difficulty.HasValue || tags.Count > 0;
    public void ClearFilters() { difficulty = null; tags.Clear(); search.SetTextWithoutNotify(""); Refresh(); }
    public void ChangeMonth(int offset)
    {
        if (owner.IsEditing || owner.HasTaskModal) return;
        if ((offset < 0 && Month.Year == 1 && Month.Month == 1) || (offset > 0 && Month.Year == 9999 && Month.Month == 12)) return;
        Month = Month.AddMonths(offset); SelectedDate = new DateTime(Month.Year, Month.Month, Math.Min(SelectedDate.Day, DateTime.DaysInMonth(Month.Year, Month.Month)));
        selected = null; noDate = false; Refresh();
    }
    public void SelectDate(DateTime date, bool daily = false, bool open = true)
    {
        if (owner.IsEditing || owner.HasTaskModal) return;
        SelectedDate = date.Date; Month = new DateTime(date.Year, date.Month, 1); noDate = false; selected = null; savedScroll = 1;
        if (open) OpenDrawer();
        Refresh();
        if (daily) { Canvas.ForceUpdateCanvases(); rowScroll.verticalNormalizedPosition = 0; }
    }
    void OpenDrawer()
    {
        if (!DrawerOpen) returnFocus = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        drawer.gameObject.SetActive(true); Layout();
    }
    public void OpenNoDate() { if (owner.IsEditing) return; noDate = true; selected = null; savedScroll = 1; OpenDrawer(); Refresh(); }
    public void OpenItem(CalendarTaskItem item)
    {
        if (owner.IsEditing || owner.HasTaskModal) return;
        savedScroll = rowScroll.verticalNormalizedPosition;
        if (item.date.HasValue) { SelectedDate = item.date.Value; Month = new DateTime(SelectedDate.Year, SelectedDate.Month, 1); }
        noDate = !item.date.HasValue; selected = item; OpenDrawer(); Refresh();
    }
    List<CalendarTaskItem> CurrentItems => noDate ? result.noDate : result.days.TryGetValue(SelectedDate, out var day) ? day.items : new List<CalendarTaskItem>();
    void RenderDrawer()
    {
        var items = CurrentItems;
        if (selected == null && rowScroll.gameObject.activeInHierarchy) savedScroll = rowScroll.verticalNormalizedPosition;
        if (selected != null) selected = items.FirstOrDefault(i => i.Key == selected.Key);
        drawerTitle.text = noDate ? "No Date" : SelectedDate.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture);
        back.gameObject.SetActive(selected != null);
        drawerAdd.gameObject.SetActive(selected == null);
        drawerBody.offsetMin = new Vector2(8, selected == null ? 64 : 8);
        detailRoot.gameObject.SetActive(selected != null); rowScroll.gameObject.SetActive(selected == null);
        undo.gameObject.SetActive(selected?.view.history != null && Service.CanUndoCompletion(selected.view.history.id));
        if (selected != null)
        {
            var item = selected;
            bool preview = item.kind == CalendarTaskKind.Preview;
            details.Render(item.view, v => preview ? "Daily preview · " + item.date.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
                : v.IsHistory ? owner.CalendarSchedule(v) : noDate ? "No schedule" : "Scheduled " + SelectedDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                Service, _ => owner.CalendarEdit(item), _ => Complete(item), owner.CalendarDelete, message => owner.Notify(message, true), preview);
            return;
        }
        if (drawerRows == null) {
            TaskUIBuilder.Clear(rows);
            drawerRows = new TaskListViewport(rows);
        }
        drawerRows.Begin();
        emptyLabel.gameObject.SetActive(items.Count == 0);
        emptyLabel.text = HasFilters ? "No matching tasks. Use Clear to reset filters." : "No tasks scheduled. Use + Add Task to get started.";
        bool? lastDaily = null;
        foreach (var item in items)
        {
            if (!noDate && lastDaily != item.view.task.daily) {
                string label = item.view.task.daily ? "Daily" : "Todo";
                drawerRows.Add("heading/"+label, "heading", 36, ()=>Instantiate(emptyLabel,rows).gameObject,
                    go=>{ go.GetComponent<TMP_Text>().text=label; TaskUIBuilder.Height((RectTransform)go.transform,36); });
                lastDaily = item.view.task.daily;
            }
            var rowRect=(RectTransform)rowTemplate.transform;
            float height=LayoutUtility.GetPreferredHeight(rowRect);
            drawerRows.Add("task/"+item.Key, "task", height>0 ? height : rowRect.rect.height,
                ()=>Instantiate(rowTemplate,rows), row=>BindDrawerRow(row,item));
        }
        if (!noDate && SelectedDate < Service.LocalNow.Date && items.Count > 0)
        {
            int missed = result.days.TryGetValue(SelectedDate, out var day) ? day.missed : 0;
            string footer = "Daily: saved records only." + (missed > 0 ? " " + missed + " missed." : "") + (HasFilters ? " Missed totals are hidden while filtering." : "");
            drawerRows.Add("footer","footer",64,()=>Instantiate(emptyLabel,rows).gameObject,
                go=>{ go.GetComponent<TMP_Text>().text=footer; TaskUIBuilder.Height((RectTransform)go.transform,64); });
        }
        if (items.Count == 0 && !noDate && SelectedDate < Service.LocalNow.Date) emptyLabel.text += " Daily: saved records only." + (result.days.TryGetValue(SelectedDate, out var past) && past.missed > 0 ? " " + past.missed + " missed." : "");
        drawerRows.Update(true);
        Canvas.ForceUpdateCanvases(); rowScroll.verticalNormalizedPosition = savedScroll;
    }
    void BindDrawerRow(GameObject row, CalendarTaskItem item)
    {
        row.name = "CalendarTask_" + item.view.task.id;
        var bindings=TaskRowBindings.For(row);
        var title=bindings.title; title.text = item.view.task.title;
        var schedule=bindings.schedule;
        schedule.text = item.kind == CalendarTaskKind.Preview ? "Daily · Preview" : item.view.Completed ? "Completed" : item.view.task.daily ? "Daily · Today" : Service.IsOverdue(item.view) ? "Overdue" : "Todo";
        schedule.text += " / " + TaskService.DifficultyName(item.view.task.difficulty);
        TaskSystemUI.SpaceSchedule(schedule);
        schedule.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 28);
        schedule.color = Service.IsOverdue(item.view) ? TaskUIBuilder.Overdue : TaskUIBuilder.ScheduleInk;
        var tick=bindings.tick; tick.gameObject.SetActive(item.view.Completed);
        var check = tick.parent.GetComponent<Button>(); check.onClick.RemoveAllListeners();
        check.interactable = item.kind != CalendarTaskKind.Preview && (!item.view.Completed || Service.CanUndoCompletion(item.view.history?.id));
        check.onClick.AddListener(() => { if (item.view.Completed) owner.CalendarUndo(item.view.history.id); else Complete(item); });
        var button = row.GetComponent<Button>(); button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => OpenItem(item));
        owner.BindTaskContext(row, item.view, item); owner.BindTaskContext(check.gameObject, item.view, item);
    }
    public void BindTaskContext(GameObject target, CalendarTaskItem item) => owner.BindTaskContext(target, item.view, item);
    void Complete(CalendarTaskItem item)
    {
        if (item.kind != CalendarTaskKind.Task) return;
        owner.CompleteForDate(item.view.task.id, item.view.task.daily ? TaskService.DateKey(item.date.Value) : null);
    }
    public void AddTask()
    {
        if (owner.IsEditing || owner.HasTaskModal) return;
        OpenDrawer(); StartEdit();
        owner.CalendarDraft(new QuestTask { scheduled = !noDate, date = TaskService.DateKey(SelectedDate), difficulty = TaskDifficulty.Normal }, Array.Empty<string>());
    }
    public void StartEdit()
    {
        editDay = TaskService.DateKey(Service.LocalNow);
        drawerBody.offsetMin = new Vector2(8, 8);
        editRect = (RectTransform)owner.editor.transform; editParent = editRect.parent; editIndex = editRect.GetSiblingIndex();
        editMin = editRect.anchorMin; editMax = editRect.anchorMax; editPivot = editRect.pivot; editPosition = editRect.anchoredPosition; editSize = editRect.sizeDelta;
        editRect.SetParent(drawerBody, false); TaskUIBuilder.Stretch(editRect);
        rowScroll.gameObject.SetActive(false); detailRoot.gameObject.SetActive(false); undo.gameObject.SetActive(false); back.gameObject.SetActive(false);
        drawerAdd.gameObject.SetActive(false);
        monthInteraction.interactable = false; listMode.interactable = calendarMode.interactable = false;
    }
    public bool ValidateDraftDate(QuestTask draft)
    {
        if (!draft.daily || editDay == TaskService.DateKey(Service.LocalNow)) return true;
        owner.Notify("The day has changed. Cancel and reopen this daily task before saving.", true); return false;
    }
    public void EndEdit()
    {
        if (editParent && editRect)
        {
            editRect.SetParent(editParent, false); editRect.SetSiblingIndex(editIndex);
            editRect.anchorMin = editMin; editRect.anchorMax = editMax; editRect.pivot = editPivot; editRect.anchoredPosition = editPosition; editRect.sizeDelta = editSize;
            editParent = null;
        }
        monthInteraction.interactable = true; listMode.interactable = calendarMode.interactable = true;
        if (Active && result != null && DrawerOpen) RenderDrawer();
    }
    public void FollowSaved(QuestTask task, string id)
    {
        selected = null;
        if (task.daily) SelectedDate = TaskRepeat.Next(task, Service.LocalNow) ?? Date(task.date);
        else if (task.scheduled) SelectedDate = Date(task.date);
        noDate = !task.daily && !task.scheduled;
        Month = new DateTime(SelectedDate.Year, SelectedDate.Month, 1); Refresh();
        selected = CurrentItems.FirstOrDefault(i => i.view.task.id == id); RenderDrawer();
        if (selected == null) emptyLabel.text = "Task saved, but hidden by the current filters.";
    }
    static DateTime Date(string value) => TaskService.TryDate(value, out var date) ? date : DateTime.Today;
    public void Back() { if (owner.IsEditing) owner.CancelDraft(); else { selected = null; RenderDrawer(); } }
    public void CloseDrawer()
    {
        if (owner.HasTaskModal) return;
        if (owner.IsEditing) owner.CancelDraft();
        drawer.gameObject.SetActive(false); selected = null; Layout();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(returnFocus && returnFocus.activeInHierarchy ? returnFocus : today.gameObject);
    }
    void LateUpdate()
    {
        if (!initialized || !Active) return;
        Layout(); details.RefreshLayout();
        if (DrawerOpen && rowScroll.gameObject.activeInHierarchy) drawerRows?.Update();
    }
    void Layout()
    {
        var bounds = ((RectTransform)transform).rect;
        float scale = GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
        bool overlay = bounds.width < 1200 || bounds.width * scale < 1200;
        float width = overlay ? Mathf.Min(480 / scale, bounds.width - 32) : Mathf.Max(400, 400 / scale);
        drawer.anchorMin = new Vector2(1, 0); drawer.anchorMax = Vector2.one;
        drawer.offsetMin = new Vector2(-width, 0); drawer.offsetMax = Vector2.zero;
        monthPanel.offsetMax = new Vector2(DrawerOpen && !overlay ? -width - 16 : 0, 0);
        backdrop.gameObject.SetActive(DrawerOpen && overlay);
        var size = grid.rect.size;
        if ((size - lastSize).sqrMagnitude < .1f) return;
        lastSize = size;
        gridLayout.cellSize = new Vector2(Mathf.Max(1, (size.x - 12) / 7), Mathf.Max(1, (size.y - 10) / 6));
        int newHeight = Mathf.FloorToInt(gridLayout.cellSize.y);
        if (newHeight != cellHeight) { cellHeight = newHeight; Refresh(); }
    }
    void Update()
    {
        if (!initialized || !Active) return;
        var pickerOverlay = jumpPicker.overlay.activeSelf ? jumpPicker.overlay : owner.IsEditing && owner.editor.datePicker.overlay.activeSelf ? owner.editor.datePicker.overlay : null;
        if (pickerOverlay) {
            if (Input.GetKeyDown(KeyCode.Escape)) pickerOverlay.SetActive(false);
            returningFromModal = true; return;
        }
        bool dropdown = owner.IsEditing && (owner.editor.tagSelection.transform.Find("Dropdown List") || owner.editor.difficulty.transform.Find("Dropdown List"));
        bool modal = owner.HasTaskModal || dropdown;
        if (modal) { returningFromModal = true; return; }
        if (returningFromModal) { returningFromModal = false; return; }
        if (Input.GetKeyDown(KeyCode.Escape)) { if (owner.IsEditing) owner.CancelDraft(); else if (selected != null) Back(); else if (DrawerOpen) CloseDrawer(); return; }
        if (owner.IsEditing) return;
        if (Input.GetKeyDown(KeyCode.Tab) && EventSystem.current)
        {
            var options = GetComponentsInChildren<Selectable>().Where(s => s.IsInteractable()
                && (DrawerOpen ? s.transform.IsChildOf(drawer) : !s.transform.IsChildOf(drawer))
                && (!s.GetComponentInParent<TaskCalendarDayUI>() || s == days.FirstOrDefault(d => d.Date == SelectedDate)?.dateButton)).ToList();
            if (!DrawerOpen) { options.Insert(0, calendarMode); options.Insert(0, listMode); }
            if (options.Count > 0) {
                int index = options.FindIndex(s => s.gameObject == EventSystem.current.currentSelectedGameObject);
                int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                EventSystem.current.SetSelectedGameObject(options[(index + direction + options.Count) % options.Count].gameObject);
            }
            return;
        }
        if (DrawerOpen) return;
        var focused = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (focused && focused.GetComponentInParent<TMP_InputField>()) return;
        if (Input.GetKeyDown(KeyCode.PageUp)) ChangeMonth(-1);
        if (Input.GetKeyDown(KeyCode.PageDown)) ChangeMonth(1);
        var cell = focused ? focused.GetComponentInParent<TaskCalendarDayUI>() : null;
        if (!cell) return;
        int offset = Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : Input.GetKeyDown(KeyCode.RightArrow) ? 1 : Input.GetKeyDown(KeyCode.UpArrow) ? -7 : Input.GetKeyDown(KeyCode.DownArrow) ? 7 : 0;
        if (offset == 0 || (offset < 0 && cell.Date < DateTime.MinValue.AddDays(-offset)) || (offset > 0 && cell.Date > DateTime.MaxValue.AddDays(-offset))) return;
        SelectDate(cell.Date.AddDays(offset), false, false);
        var target = days.FirstOrDefault(d => d.Date == SelectedDate);
        if (target) EventSystem.current.SetSelectedGameObject(target.dateButton.gameObject);
    }
    void OnDisable() { if (jumpPicker && jumpPicker.overlay) jumpPicker.overlay.SetActive(false); }
}
