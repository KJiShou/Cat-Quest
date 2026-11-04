using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

public partial class TaskSystemUI : MonoBehaviour
{
    public EditTaskDetailsUI editor;
    public RectTransform listContent;
    public RectTransform detailsRoot;
    public TMP_InputField search;
    public Button filterButton;
    public TMP_Text listHeading;
    public Button[] tabButtons;
    public TMP_Text[] tabLabels;
    public TMP_Text[] tabCounts;
    public GameObject[] tabFocus;
    public TMP_Text fishCoins;
    [FormerlySerializedAs("experience")] public TMP_Text tickets;
    public GameObject taskRowTemplate;
    public GameObject addTaskTemplate;
    public GameObject inlineTaskTemplate;
    public GameObject confirmDialogTemplate;
    public TMP_InputField QuickAddInput => quickInput;
    public bool TutorialBlocked => editing || (confirmation != null && confirmation.Root.activeInHierarchy) || (filters && filters.gameObject.activeInHierarchy);
    public bool TutorialNoticeVisible => resultNotice && resultNotice.gameObject.activeInHierarchy;
    public RectTransform TutorialTaskTarget(string id)
    {
        var row = listContent.Cast<Transform>().FirstOrDefault(t => t.gameObject.activeInHierarchy && t.name == "Task_" + id);
        if (row) return row as RectTransform;
        var revealed = isActiveAndEnabled && (calendar == null || !calendar.Active) ? rows?.RevealTask(id) : null;
        return revealed ? revealed.transform as RectTransform : null;
    }
    public RectTransform TutorialCompletionTarget(string id)
    {
        var row = TutorialTaskTarget(id);
        var tick = row ? row.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Tick") : null;
        return tick ? tick.parent as RectTransform : row;
    }
    public RectTransform TutorialAddTarget => quickInput ? quickInput.transform as RectTransform : listContent.Cast<Transform>().FirstOrDefault(t => t.gameObject.activeInHierarchy && t.name == "AddTask") as RectTransform;
    public Sprite roundedSprite;
    public Sprite tagTintSprite;
    public TMP_FontAsset font;
    public TaskService Service { get; private set; }
    public TaskTab CurrentTab { get; private set; }
    public string SelectedKey { get; private set; }
    private TaskUIBuilder ui;
    private TaskListViewport rows;
    private GameObject addRow;
    private string rowGroup;
    private TaskDifficulty? difficultyFilter;
    private readonly HashSet<string> tagFilters = new HashSet<string>();
    private List<TaskView> visible = new List<TaskView>();
    private TaskDetailsPresenter details;
    private TMP_InputField quickInput;
    private QuestTask quickDraft;
    private bool committingQuick;
    private bool pendingRefresh;
    private bool refocusQuick;
    private TaskConfirmDialog confirmation;
    private TaskResultNotice resultNotice;
    public TaskFilterDialog filterDialogTemplate;
    private TaskFilterDialog filters;
    private float nextClockCheck;
    private bool editing;
    private readonly List<RaycastResult> outsideClickHits = new List<RaycastResult>();
    private string outsideClickSelection;

    private void Start()
    {
        var tabBar=tabButtons[0].transform.parent;
        var tabs=tabBar.GetComponent<TaskTabsLayout>() ?? tabBar.gameObject.AddComponent<TaskTabsLayout>();
        tabs.owner=this;tabs.Apply();
        ui = new TaskUIBuilder(font, roundedSprite);
        TaskUIBuilder.Clear(listContent);
        rows = new TaskListViewport(listContent);
        var manager = GameDataManager.EnsureInstance();
        Service = manager.Tasks;
        Service.Changed += Refresh;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            tabButtons[i].onClick.AddListener(() => SelectTab((TaskTab)index));
            foreach(var child in tabButtons[i].GetComponentsInChildren<Button>(true))
                if(child!=tabButtons[i]) { child.onClick.RemoveAllListeners(); child.onClick.AddListener(()=>SelectTab((TaskTab)index)); }
        }
        search.textComponent.fontSize=16; search.textComponent.enableAutoSizing=false;
        if(search.placeholder is TMP_Text hint) { hint.fontSize=16; hint.enableAutoSizing=false; }
        search.SetTextWithoutNotify("");
        TaskUIBuilder.BlueCaret(search);
        search.onValueChanged.AddListener(_ => Refresh());
        filterButton.onClick.AddListener(ShowFilters);
        foreach(var child in filterButton.GetComponentsInChildren<Button>(true))
            if(child!=filterButton) { child.onClick.RemoveAllListeners(); child.onClick.AddListener(ShowFilters); }
        editor.transform.Find("Footer/Actions/SaveChangesButton").GetComponent<Button>().onClick.AddListener(SaveDraft);
        editor.transform.Find("Footer/Actions/CancelButton").GetComponent<Button>().onClick.AddListener(CancelDraft);
        editor.transform.Find("Header/Close").GetComponent<Button>().onClick.AddListener(CancelDraft);
        BuildDetails(); BuildModal();
        editor.gameObject.SetActive(false); detailsRoot.gameObject.SetActive(false);
        CurrentTab = Service.Settings.DefaultTaskTab;
        if (calendar) calendar.Initialize(this); else Refresh();
        if (!string.IsNullOrEmpty(manager.LoadNotice)) Notify(manager.LoadNotice, true);
        else if (!string.IsNullOrEmpty(Service.LastError)) Notify(Service.LastError, true);
    }
    private void Update()
    {
        if (Service == null) return;
        if(Input.GetMouseButtonDown(0) && EventSystem.current)
        {
            outsideClickHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position=Input.mousePosition },outsideClickHits);
            CaptureOutsideClick(outsideClickHits.Count>0 ? outsideClickHits[0].gameObject.transform : null);
        }
        if(quickInput && Input.GetKeyDown(KeyCode.Escape)) CancelQuickAdd();
        if (Time.unscaledTime >= nextClockCheck)
        {
            nextClockCheck = Time.unscaledTime + 1;
            if (!Service.RefreshDate()) Notify(Service.LastError,true);
            details.RefreshScheduleColor(Service);
        }
    }
    private void OnEnable()
    {
        if(Service!=null) { Service.RefreshDate(); Service.Changed += Refresh; if (calendar && calendar.Active) Refresh(); else SelectTab(Service.Settings.DefaultTaskTab); }
    }
    public bool PrepareToLeavePage()
    {
        if(!CommitQuickAdd()) return false;
        CancelDraft(); CloseModal(); confirmation?.Dismiss();
        return true;
    }
    private void OnApplicationFocus(bool focus) { if (focus && isActiveAndEnabled && Service != null) { Service.RefreshDate(); Refresh(); } }
    private void OnDestroy() { if (Service != null) Service.Changed -= Refresh; if(filters) Destroy(filters.gameObject); if(resultNotice) Destroy(resultNotice.gameObject); if(confirmation?.Root) Destroy(confirmation.Root); }

    private void BuildDetails()
    {
        details = new TaskDetailsPresenter(detailsRoot, ui, tagTintSprite);
    }
    private void LateUpdate()
    {
        // Let the clicked control handle its event before expanding the list underneath it.
        if (calendar == null || !calendar.Active) rows?.Update();
        if(outsideClickSelection!=null)
        {
            string selection=outsideClickSelection; outsideClickSelection=null;
            if(!editing && SelectedKey==selection) { SelectedKey=null; Refresh(); }
        }
        details?.RefreshLayout();
        if (pendingRefresh) { pendingRefresh=false; Refresh(); }
        if(refocusQuick && quickInput) { refocusQuick=false; quickInput.Select(); quickInput.ActivateInputField(); }
    }
    private void CaptureOutsideClick(Transform target)
    {
        if((calendar && calendar.Active) || editing || SelectedKey==null || !detailsRoot.gameObject.activeInHierarchy
            || (filters && filters.gameObject.activeSelf) || confirmation.Root.activeSelf) return;
        if(target && target.IsChildOf(detailsRoot)) return;
        for(var current=target; current && current.IsChildOf(listContent); current=current.parent)
            if(current.name.StartsWith("Task_",StringComparison.Ordinal)) return;
        outsideClickSelection=SelectedKey;
    }
    private void BuildModal()
    {
        var canvas=GetComponentInParent<Canvas>().rootCanvas;
        filters=Instantiate(filterDialogTemplate,canvas.transform);
        filters.gameObject.SetActive(false);
        confirmation=new TaskConfirmDialog(confirmDialogTemplate,canvas.transform,font);
        resultNotice=Instantiate(Resources.Load<TaskResultNotice>("TaskResultNotice"),canvas.transform);
        resultNotice.Dismiss();
    }
    public void Notify(string message,bool error=false)
    {
        if(error) { resultNotice.Dismiss(); confirmation.ShowNotice("Notice",message); }
        else resultNotice.Show(message);
    }
    private void CloseModal() { if(filters) filters.Dismiss(); }
    private void OnDisable() { if (Service != null) Service.Changed -= Refresh; CloseModal(); confirmation?.Dismiss(); if(resultNotice)resultNotice.Dismiss(); }
    private void Confirm(string title,string message,Action accepted,string confirmLabel="Confirm")
    {
        CloseModal(); confirmation.Show(title,message,accepted,confirmLabel);
    }
    public void SelectTab(TaskTab tab)
    {
        if(!CommitQuickAdd()) return;
        CancelDraft(); CurrentTab=tab; SelectedKey=null; difficultyFilter=null; tagFilters.Clear(); search.SetTextWithoutNotify(""); Refresh();
    }
    public void Refresh()
    {
        if(Service==null || ui==null) return;
        if(committingQuick) { pendingRefresh=true; return; }
        pendingRefresh=false;
        if (calendar && calendar.Active) { calendar.Refresh(); return; }
        var data=Service.Snapshot;
        tagFilters.RemoveWhere(id=>!data.tags.Any(t=>t.id==id));
        for(int i=0;i<5;i++)
        {
            int count = (TaskTab)i switch {
                TaskTab.Tags => data.tags.Count,
                TaskTab.History => data.history.Count(h=>!h.revoked),
                TaskTab.Todo => QueryTodo(data).Count,
                _ => Service.QuerySnapshot(data,(TaskTab)i).Count
            };
            bool active=i==(int)CurrentTab;
            Color color=active ? TaskUIBuilder.Blue : new Color32(121,143,173,255);
            tabCounts[i].text=count.ToString(); tabCounts[i].color=Color.white;
            tabCounts[i].transform.parent.gameObject.SetActive(i<3);
            tabLabels[i].color=color; tabFocus[i].SetActive(active);
            var icon=tabButtons[i].transform.Find("Icon").GetComponent<Image>();
            icon.color=color;
            var badge=tabCounts[i].transform.parent.GetComponent<Image>(); if(badge) badge.color=color;
            foreach(var button in tabButtons[i].GetComponentsInChildren<Button>(true)) button.transition=Selectable.Transition.None;
        }
        fishCoins.text=data.fishCoins.ToString(); tickets.text=data.tickets.ToString();
        listHeading.text=new[]{"Today's Tasks","Daily Tasks","Todo","Tags","History"}[(int)CurrentTab];
        visible=CurrentTab == TaskTab.Todo ? QueryTodo(data,search.text,difficultyFilter,tagFilters) : Service.QuerySnapshot(data,CurrentTab,search.text,difficultyFilter,tagFilters);
        rowGroup = "";
        if (!addRow) {
            addRow = Instantiate(addTaskTemplate, listContent); addRow.name = "AddTask";
            var button = addRow.GetComponent<Button>() ?? addRow.AddComponent<Button>();
            button.onClick.RemoveAllListeners(); button.onClick.AddListener(BeginAdd);
        }
        bool showAdd = CurrentTab != TaskTab.History && !quickInput;
        addRow.SetActive(showAdd);
        if (quickInput) quickInput.transform.parent.SetAsFirstSibling();
        else if (showAdd) addRow.transform.SetAsFirstSibling();
        rows.Begin(quickInput || showAdd ? 1 : 0, quickInput ? 80 : showAdd ? LayoutUtility.GetPreferredHeight((RectTransform)addRow.transform) : 0);
        if(visible.Count==0 && !quickInput) {
            string message = CurrentTab==TaskTab.History ? "No completed tasks yet." : "No tasks here. Add a task or adjust your filters.";
            rows.Add("empty","empty",28,()=>ui.Text(listContent,message,20,new Color32(123,145,171,255)).gameObject,
                go=>go.GetComponent<TMP_Text>().text=message);
        }
        if(CurrentTab==TaskTab.Todo) RenderDateGroups();
        else if(CurrentTab==TaskTab.Tags) RenderTagGroups(data.tags);
        else foreach(var row in visible) RenderRow(row);
        rows.Update(true);
        var selected=visible.FirstOrDefault(v=>v.Key==SelectedKey);
        if(selected==null) SelectedKey=null;
        if(editing) editor.RefreshCatalog(data.tags); else RenderDetails(selected);
        LayoutRebuilder.MarkLayoutForRebuild(listContent);
    }
    private void RenderRow(TaskView view)
    {
        rows.Add("task/"+rowGroup+"/"+view.Key, "task", 80, ()=>Instantiate(taskRowTemplate,listContent), row=>BindTaskRow(row,view));
    }
    private void BindTaskRow(GameObject row, TaskView view)
    {
        row.name="Task_"+view.Key; row.SetActive(true);
        var rect=row.GetComponent<RectTransform>(); TaskUIBuilder.Height(rect,80); TaskUIBuilder.Flex(rect);
        var bindings=TaskRowBindings.For(row);
        var title=bindings.title; title.text=view.task.title;
        title.textWrappingMode=TextWrappingModes.NoWrap; title.overflowMode=TextOverflowModes.Ellipsis;
        TaskUIBuilder.Flex(title.transform.parent);
        var date=bindings.schedule; date.text=ScheduleText(view);
        date.color=Service.IsOverdue(view) ? TaskUIBuilder.Overdue : TaskUIBuilder.ScheduleInk;
        SpaceSchedule(date);
        var difficulty=bindings.difficulty;
        TaskUIBuilder.DifficultyBadge(difficulty,view.task.difficulty,editor.difficultySprites);
        var reward=Service.PreviewReward(view);
        // DashboardTask's authored second numeric label belongs to the EXP icon.
        if (bindings.fish) bindings.fish.text=reward.fishCoins.ToString();
        if (bindings.experience) bindings.experience.text=reward.experience.ToString();
        var tags=bindings.tags;
        var secondTag = tags.parent.Find("Tag_1");
        var moreTags = tags.parent.Find("MoreTags");
        if(secondTag) secondTag.gameObject.SetActive(view.tagNames.Count > 1);
        if(moreTags) moreTags.gameObject.SetActive(view.tagNames.Count > 2);
        for (int i=0;i<Math.Min(2,view.tagNames.Count);i++)
        {
            var chip=i==0 ? tags.gameObject : secondTag ? secondTag.gameObject : Instantiate(tags.gameObject,tags.parent);
            chip.SetActive(true);
            if(i>0) { chip.name="Tag_"+i; chip.transform.SetSiblingIndex(tags.GetSiblingIndex()+i); }
            TaskUIBuilder.LayoutTag(chip,view.tagNames[i],15,28);
            var chipSize=chip.GetComponent<LayoutElement>();
            chipSize.minWidth=chipSize.preferredWidth=Mathf.Min(140,chipSize.preferredWidth);
            chip.GetComponentInChildren<TMP_Text>().overflowMode=TextOverflowModes.Ellipsis;
            TagColors.Apply(chip,view.tagColors!=null && i<view.tagColors.Count ? view.tagColors[i] : TagColors.DefaultHex,tagTintSprite);
        }
        if(view.tagNames.Count>2)
        {
            var extra=moreTags ? moreTags.gameObject : Instantiate(tags.gameObject,tags.parent); extra.name="MoreTags";
            extra.SetActive(true);
            extra.transform.SetSiblingIndex(tags.GetSiblingIndex()+2);
            TaskUIBuilder.LayoutTag(extra,"+"+(view.tagNames.Count-2),15,28);
            TagColors.Apply(extra,TagColors.DefaultHex,tagTintSprite);
        }
        tags.gameObject.SetActive(view.tagNames.Count>0);
        var tick=bindings.tick; if(tick) tick.gameObject.SetActive(view.Completed);
        var checkbox=tick.parent.GetComponent<Button>() ?? tick.parent.gameObject.AddComponent<Button>();
        checkbox.targetGraphic=tick.parent.GetComponent<Image>(); checkbox.onClick.RemoveAllListeners();
        tick.GetComponent<Image>().raycastTarget=false;
        if(view.Completed)
        {
            string historyId=view.IsHistory ? view.history.id : Service.CompletionId(view.task.id,view.occurrence.day);
            checkbox.interactable=Service.CanUndoCompletion(historyId);
            checkbox.onClick.AddListener(()=>
            {
                if(!CommitQuickAdd()) return;
                if(!Service.UndoCompletion(historyId)) Notify(Service.LastError,true);
                else Notify("Task restored. Previously earned rewards are kept.");
            });
        }
        else
        {
            checkbox.interactable=Service.Eligible(view.task);
            checkbox.onClick.AddListener(()=> { if(CommitQuickAdd()) CompleteSelected(view.task.id); });
        }
        var button=row.GetComponent<Button>() ?? row.AddComponent<Button>(); button.onClick.RemoveAllListeners(); button.onClick.AddListener(()=>SelectTask(view.Key));
        var background=row.GetComponent<Image>(); if(background) background.color=view.Key==SelectedKey ? new Color32(179,235,255,255) : Color.white;
    }
    private string ScheduleText(TaskView view)
    {
        if(view.IsHistory && DateTime.TryParse(view.history.completedUtc,out var completed)) return "Completed " + completed.ToLocalTime().ToString("d MMM yyyy HH:mm",System.Globalization.CultureInfo.InvariantCulture);
        if(!view.task.scheduled) return "No schedule";
        TaskService.TryDate(view.task.date, out var date);
        string prefix = view.Completed ? "Done today | " : "";
        if (view.task.daily)
        {
            var next = TaskRepeat.Next(view.task, Service.LocalNow);
            if (!next.HasValue) return "Repeat ended";
            if (next.Value > Service.LocalNow.Date) prefix = next.Value == date ? "Starts " : "Next ";
            date = next.Value;
        }
        return prefix + date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }
    public void SelectTask(string key) { if(!CommitQuickAdd()) return; SelectedKey=key; editing=false; editor.gameObject.SetActive(false); detailsRoot.gameObject.SetActive(true); Refresh(); }
    private void RenderDetails(TaskView view)
    {
        detailsRoot.gameObject.SetActive(!editing && view!=null);
        if(view==null) return;
        details.Render(view, ScheduleText, Service, BeginEdit, CompleteSelected,
            id=>Confirm("Delete task?", "Completed history and rewards will be kept.",
                ()=> { if(!Service.Delete(id)) Notify(Service.LastError,true); else Notify("Task deleted."); },"Delete"),
            message=>Notify(message,true));

    }
    internal static void SpaceSchedule(TMP_Text date)
    {
        date.textWrappingMode=TextWrappingModes.NoWrap;
        var size=date.GetComponent<LayoutElement>() ?? date.gameObject.AddComponent<LayoutElement>();
        size.minWidth=size.preferredWidth=Mathf.Ceil(date.GetPreferredValues().x); size.flexibleWidth=0;
        var layout=date.transform.parent.GetComponent<HorizontalLayoutGroup>();
        if(layout) {
            layout.childControlWidth=true; layout.childForceExpandWidth=false; layout.spacing=16;
            var icon=date.transform.parent.Find("ScheduleIcon");
            if(icon) {
                var iconSize=icon.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
                iconSize.minWidth=iconSize.preferredWidth=20; iconSize.flexibleWidth=0;
                var image=icon.GetComponent<Image>(); if(image) image.preserveAspect=true;
            }
        }
    }
    public void CompleteSelected(string id) => CompleteForDate(id, null);
    public void CompleteForDate(string id, string expectedDay)
    {
        Action complete=()=>
        {
            if (expectedDay != null && expectedDay != TaskService.DateKey(Service.LocalNow)) { Notify("The day has changed. Select today to complete this daily task.",true); Refresh(); return; }
            var before=Service.Query(TaskTab.Daily).Concat(Service.Query(TaskTab.Todo)).FirstOrDefault(v=>v.task.id==id);
            if(!Service.Complete(id,true)) Notify(Service.LastError,true);
            else {
                if(before==null || before.Completed) { Notify("Task already completed. No additional rewards."); return; }
                SettingsAudio.Instance?.PlayTaskComplete();
                if(before.occurrence.rewardGranted) Notify("Task completed again. No additional rewards; previous rewards are kept.");
                else {
                    var receipt=Service.Snapshot.history.First(h=>h.taskId==id && h.day==before.occurrence.day);
                    Notify(receipt.experience==0 && receipt.fishCoins==0 && receipt.tickets==0
                        ? "Task completed. Daily task reward limit reached; no additional rewards."
                        : $"Task completed. +{receipt.experience} EXP, +{receipt.fishCoins} Fish Coins." + (receipt.tickets > 0 ? " +1 Ticket." : ""));
                }
            }
        };
        if(Service.HasIncompleteSubtasks(id)) Confirm("Complete remaining subtasks?","Some subtasks are unfinished. Confirm to complete them all and collect the task rewards.",complete); else complete();
    }
    public void BeginAdd()
    {
        if(quickInput) { quickInput.Select(); quickInput.ActivateInputField(); return; }
        CancelDraft();
        var now=Service.LocalNow;
        quickDraft=new QuestTask { title="",difficulty=TaskDifficulty.Normal,
            daily=CurrentTab==TaskTab.Daily,scheduled=CurrentTab==TaskTab.Today || CurrentTab==TaskTab.Daily,
            date=TaskService.DateKey(now),tagIds=tagFilters.ToList() };
        var row=Instantiate(inlineTaskTemplate,listContent); row.name="QuickAddTask"; row.SetActive(true);
        TaskUIBuilder.Height(row.GetComponent<RectTransform>(),80); TaskUIBuilder.Flex(row.transform);
        quickInput=row.GetComponentInChildren<TMP_InputField>(true);
        quickInput.characterLimit=100; quickInput.lineType=TMP_InputField.LineType.SingleLine;
        TaskUIBuilder.BlueCaret(quickInput);
        quickInput.SetTextWithoutNotify("");
        var date=row.transform.Find("Title/SecondRow/DateTime").GetComponent<TMP_Text>();
        date.text=ScheduleText(new TaskView { task=quickDraft, occurrence=new TaskOccurrence() }); SpaceSchedule(date);
        row.transform.Find("Title/SecondRow/Tags").gameObject.SetActive(false);
        quickInput.onSubmit.RemoveAllListeners(); quickInput.onDeselect.RemoveAllListeners();
        quickInput.onSubmit.AddListener(_=>CommitQuickAdd(true));
        quickInput.onDeselect.AddListener(_=> {
            if(refocusQuick) return;
            if(quickInput && quickInput.wasCanceled) CancelQuickAdd();
            else if(!PointerOverAdd()) CommitQuickAdd();
        });
        Refresh(); quickInput.Select(); quickInput.ActivateInputField();
    }
    private bool PointerOverAdd()
    {
        if(!EventSystem.current) return false;
        var pointer=new PointerEventData(EventSystem.current) { position=Input.mousePosition };
        var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
        if(hits.Count==0) return false;
        for(var t=hits[0].gameObject.transform; t && t!=listContent; t=t.parent)
            if(t.name=="AddTask") return true;
        return false;
    }
    public bool CommitQuickAdd(bool continueAdding=false)
    {
        if(!quickInput || committingQuick) return true;
        if(string.IsNullOrWhiteSpace(quickInput.text)) {
            if(continueAdding) refocusQuick=true; else CancelQuickAdd(); return true;
        }
        quickDraft.title=quickInput.text.Trim(); committingQuick=true;
        bool saved=Service.Upsert(quickDraft,Array.Empty<string>(),out var id);
        committingQuick=false;
        if(!saved) { pendingRefresh=false; refocusQuick=continueAdding; Notify(Service.LastError,true); return false; }
        SelectedKey=id;
        if(continueAdding) {
            quickDraft.id=null; quickDraft.title=""; quickInput.SetTextWithoutNotify("");
            pendingRefresh=true; refocusQuick=true;
        } else CancelQuickAdd();
        Notify("Task saved."); return true;
    }
    public void CancelQuickAdd()
    {
        if(!quickInput) return;
        var row=quickInput.transform.parent.gameObject;
        quickInput.onSubmit.RemoveAllListeners(); quickInput.onDeselect.RemoveAllListeners();
        quickInput=null; quickDraft=null; refocusQuick=false; row.SetActive(false); Destroy(row); pendingRefresh=true;
    }
    private void BeginEdit(TaskView view) => OpenDraft(view.task,view.occurrence.completedSubtaskIds,"Edit Task");
    private void OpenDraft(QuestTask task,IEnumerable<string> done,string title)
    {
        editing=true; detailsRoot.gameObject.SetActive(false); editor.gameObject.SetActive(true);
        editor.LoadDraft(PlayerDataStore.Copy(task),Service.TagCatalog,done);
        editor.transform.Find("Header/Heading").GetComponent<TMP_Text>().text=title;
        editor.transform.Find("Footer/Actions/SaveChangesButton").GetComponentInChildren<TMP_Text>().text=string.IsNullOrEmpty(task.id) ? "Add Task" : "Save Changes";
    }
    public void SaveDraft()
    {
        var draft=editor.ReadDraft(out var done);
        if (calendar && calendar.Active && !calendar.ValidateDraftDate(draft)) return;
        if(!Service.Upsert(draft,done,out var id)) { Notify(Service.LastError,true); return; }
        SelectedKey=id; CancelDraft();
        if (calendar && calendar.Active) calendar.FollowSaved(draft, id);
        Refresh(); Notify("Task saved.");
    }
    public void CancelDraft()
    {
        editing=false; editor.datePicker.overlay.SetActive(false); editor.timePicker.overlay.SetActive(false);
        editor.gameObject.SetActive(false);
        if (calendar && calendar.Active) calendar.EndEdit();
        else if(Service!=null) RenderDetails(visible.FirstOrDefault(v=>v.Key==SelectedKey));
    }
    private List<TaskView> QueryTodo(PlayerData data, string text = "", TaskDifficulty? difficulty = null, IEnumerable<string> tags = null)
    {
        return Service.QuerySnapshot(data,TaskTab.Todo,text,difficulty,tags)
            .Concat(Service.QuerySnapshot(data,TaskTab.Daily,text,difficulty,tags).Where(view => !view.Completed)).ToList();
    }
    private void RenderDateGroups()
    {
        foreach(var group in visible.GroupBy(view => TaskDateGroups.Classify(view.task,Service.LocalNow)).OrderBy(group => group.Key))
        {
            RenderTagHeading("date_"+group.Key,TaskDateGroups.Caption(group.Key),group.Count());
            foreach(var task in group) RenderRow(task);
        }
    }
    private void RenderTagGroups(List<QuestTag> tags)
    {
        foreach(var tag in tags) {
            var tasks=visible.Where(view=>view.task.tagIds.Contains(tag.id)).ToList();
            if(tasks.Count==0) continue;
            RenderTagHeading(tag.id,tag.name,tasks.Count,tag.colorHex);
            foreach(var task in tasks) RenderRow(task);
        }
        var untagged=visible.Where(view=>view.task.tagIds.Count==0).ToList();
        if(untagged.Count>0) {
            RenderTagHeading("untagged","No Tags",untagged.Count);
            foreach(var task in untagged) RenderRow(task);
        }
    }
    private void RenderTagHeading(string id,string caption,int count,string colorHex=null)
    {
        rowGroup = id;
        rows.Add("heading/"+id, "heading", 42, () => {
            var header=ui.Row(listContent,"Group heading",42,10);
            var source=detailsRoot.Find("ScrollRect/Content/Tags/Title/Icon");
            var icon=Instantiate(source.gameObject,header); icon.SetActive(true);
            var size=TaskUIBuilder.Height(icon.GetComponent<RectTransform>(),26);
            size.minWidth=size.preferredWidth=26; size.flexibleWidth=0;
            foreach(var button in icon.GetComponents<Button>()) { button.onClick.RemoveAllListeners(); button.enabled=false; }
            ui.Text(header,"",22,TaskUIBuilder.Ink);
            return header.gameObject;
        }, header => {
            header.name="TagGroup_"+id;
            var icon=header.transform.GetChild(0).GetComponent<Image>();
            icon.color=colorHex!=null ? TagColors.Background(colorHex) : detailsRoot.Find("ScrollRect/Content/Tags/Title/Icon").GetComponent<Image>().color;
            header.GetComponentInChildren<TMP_Text>().text=caption+" ("+count+")";
        });
    }
    private void ShowFilters()
    {
        if(!CommitQuickAdd()) return;
        filters.Show(difficultyFilter,tagFilters,Service.TagCatalog,(difficulty,tags)=> {
            difficultyFilter=difficulty; tagFilters.Clear(); tagFilters.UnionWith(tags); Refresh();
        });
    }
}
