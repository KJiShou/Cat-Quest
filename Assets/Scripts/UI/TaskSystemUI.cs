using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

public class TaskSystemUI : MonoBehaviour
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
    public Sprite roundedSprite;
    public TMP_FontAsset font;
    public TaskService Service { get; private set; }
    public TaskTab CurrentTab { get; private set; }
    public string SelectedKey { get; private set; }
    private TaskUIBuilder ui;
    private TaskDifficulty? difficultyFilter;
    private readonly HashSet<string> tagFilters = new HashSet<string>();
    private List<TaskView> visible = new List<TaskView>();
    private TaskDetailsPresenter details;
    private TMP_InputField quickInput;
    private QuestTask quickDraft;
    private bool committingQuick;
    private bool pendingRefresh;
    private bool refocusQuick;
    private bool suppressQuickBlur;
    private TaskConfirmDialog confirmation;
    private RectTransform modalOverlay;
    private RectTransform modalPanel;
    private float nextClockCheck;
    private bool editing;
    private readonly List<(TMP_Text label, TaskView view)> scheduleLabels = new List<(TMP_Text, TaskView)>();
    private readonly List<RaycastResult> outsideClickHits = new List<RaycastResult>();
    private string outsideClickSelection;

    private void Start()
    {
        var tabBar=tabButtons[0].transform.parent;
        var tabs=tabBar.GetComponent<TaskTabsLayout>() ?? tabBar.gameObject.AddComponent<TaskTabsLayout>();
        tabs.owner=this;tabs.Apply();
        ui = new TaskUIBuilder(font, roundedSprite);
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
        CurrentTab = TaskTab.Today;
        Refresh();
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
            foreach (var item in scheduleLabels)
                if (item.label) item.label.color = Service.IsOverdue(item.view) ? TaskUIBuilder.Overdue : TaskUIBuilder.ScheduleInk;
            details.RefreshScheduleColor(Service);
        }
    }
    private void OnEnable()
    {
        if(Service!=null) { Service.RefreshDate(); Refresh(); }
    }
    public bool PrepareToLeavePage()
    {
        if(!CommitQuickAdd()) return false;
        CancelDraft(); CloseModal(); confirmation?.Dismiss();
        return true;
    }
    private void OnApplicationFocus(bool focus) { if (focus && Service != null) { Service.RefreshDate(); Refresh(); } }
    private void OnDestroy() { if (Service != null) Service.Changed -= Refresh; }

    private void BuildDetails()
    {
        details = new TaskDetailsPresenter(detailsRoot, ui);
    }
    private void LateUpdate()
    {
        // Let the clicked control handle its event before expanding the list underneath it.
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
        if(editing || SelectedKey==null || !detailsRoot.gameObject.activeInHierarchy
            || modalOverlay.gameObject.activeSelf || confirmation.Root.activeSelf) return;
        if(target && target.IsChildOf(detailsRoot)) return;
        for(var current=target; current && current.IsChildOf(listContent); current=current.parent)
            if(current.name.StartsWith("Task_",StringComparison.Ordinal)) return;
        outsideClickSelection=SelectedKey;
    }
    private void BuildModal()
    {
        var canvas=GetComponentInParent<Canvas>().rootCanvas;
        modalOverlay=TaskUIBuilder.Rect(canvas.transform,"TaskDialog"); TaskUIBuilder.Stretch(modalOverlay); ui.Image(modalOverlay,new Color(0,0,0,.08f));
        var close=modalOverlay.gameObject.AddComponent<Button>(); close.onClick.AddListener(CloseModal);
        close.gameObject.AddComponent<JuicyButton>().enabled=false; // Backdrop dismiss surface, not a visible button.
        modalPanel=ui.Column(modalOverlay,"Dialog",-1,12); modalPanel.anchorMin=modalPanel.anchorMax=new Vector2(.5f,.5f); modalPanel.pivot=new Vector2(.5f,.5f); modalPanel.sizeDelta=new Vector2(540,440);
        ui.Image(modalPanel,Color.white,roundedSprite); modalPanel.gameObject.AddComponent<Button>();
        modalPanel.gameObject.AddComponent<JuicyButton>().enabled=false; // Swallow panel clicks without animating the whole dialog.
        modalPanel.GetComponent<VerticalLayoutGroup>().padding=new RectOffset(24,24,20,20);
        modalOverlay.gameObject.SetActive(false);
        confirmation=new TaskConfirmDialog(confirmDialogTemplate,canvas.transform,font);
    }
    public void Notify(string message,bool error=false)
    {
        if(error) confirmation.ShowNotice("Notice",message);
    }
    private void OpenModal(string title,float height=440)
    {
        TaskUIBuilder.Clear(modalPanel); modalPanel.sizeDelta=new Vector2(540,height); modalOverlay.gameObject.SetActive(true); modalOverlay.SetAsLastSibling(); ui.Text(modalPanel,title,26);
    }
    private void CloseModal() => modalOverlay.gameObject.SetActive(false);
    private void Confirm(string title,string message,Action accepted)
    {
        CloseModal(); confirmation.Show(title,message,accepted);
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
        var data=Service.Snapshot;
        tagFilters.RemoveWhere(id=>!data.tags.Any(t=>t.id==id));
        for(int i=0;i<5;i++)
        {
            int count=i==(int)TaskTab.Tags ? data.tags.Count : Service.Query((TaskTab)i).Count;
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
        visible=Service.Query(CurrentTab,search.text,difficultyFilter,tagFilters);
        scheduleLabels.Clear();
        suppressQuickBlur=true;
        if(quickInput) quickInput.transform.parent.SetParent(transform,false);
        TaskUIBuilder.Clear(listContent);
        if(CurrentTab!=TaskTab.History && !quickInput)
        {
            var add=Instantiate(addTaskTemplate,listContent); add.name="AddTask"; add.SetActive(true);
            var button=add.GetComponent<Button>() ?? add.AddComponent<Button>(); button.onClick.RemoveAllListeners(); button.onClick.AddListener(BeginAdd);
        }
        if(quickInput) { quickInput.transform.parent.SetParent(listContent,false); quickInput.transform.parent.SetSiblingIndex(0); }
        if(visible.Count==0 && !quickInput) ui.Text(listContent,CurrentTab==TaskTab.History ? "No completed tasks yet." : "No tasks here. Add a task or adjust your filters.",20,new Color32(123,145,171,255));
        if(CurrentTab==TaskTab.Tags) RenderTagGroups(data.tags);
        else foreach(var row in visible) RenderRow(row);
        suppressQuickBlur=false;
        var selected=visible.FirstOrDefault(v=>v.Key==SelectedKey);
        if(selected==null) SelectedKey=null;
        if(editing) editor.RefreshCatalog(data.tags); else RenderDetails(selected);
        LayoutRebuilder.MarkLayoutForRebuild(listContent);
    }
    private void RenderRow(TaskView view)
    {
        var row=Instantiate(taskRowTemplate,listContent); row.name="Task_"+view.Key; row.SetActive(true);
        var rect=row.GetComponent<RectTransform>(); TaskUIBuilder.Height(rect,80); TaskUIBuilder.Flex(rect);
        var title=TaskUIBuilder.Find(row.transform,"TaskName").GetComponent<TMP_Text>(); title.text=view.task.title;
        title.textWrappingMode=TextWrappingModes.NoWrap; title.overflowMode=TextOverflowModes.Ellipsis;
        TaskUIBuilder.Flex(title.transform.parent);
        var date=TaskUIBuilder.Find(row.transform,"DateTime").GetComponent<TMP_Text>(); date.text=ScheduleText(view);
        date.color=Service.IsOverdue(view) ? TaskUIBuilder.Overdue : TaskUIBuilder.ScheduleInk;
        scheduleLabels.Add((date,view));
        SpaceSchedule(date);
        var difficulty=TaskUIBuilder.Find(row.transform,"Normal_Difficulty");
        TaskUIBuilder.DifficultyBadge(difficulty,view.task.difficulty,editor.difficultySprites);
        foreach(var text in row.GetComponentsInChildren<TMP_Text>(true)) if(text.name.StartsWith("FishNum")) text.text=Service.PreviewReward(view).fishCoins.ToString();
        var tags=TaskUIBuilder.Find(row.transform,"Tags");
        string tagCaption=string.Join(", ",view.tagNames.Take(2))+(view.tagNames.Count>2 ? " +"+(view.tagNames.Count-2) : "");
        TaskUIBuilder.LayoutTag(tags.gameObject,tagCaption,15,28);
        tags.gameObject.SetActive(view.tagNames.Count>0);
        var tick=TaskUIBuilder.Find(row.transform,"Tick"); if(tick) tick.gameObject.SetActive(view.Completed);
        var checkbox=tick.parent.GetComponent<Button>() ?? tick.parent.gameObject.AddComponent<Button>();
        checkbox.targetGraphic=tick.parent.GetComponent<Image>(); checkbox.onClick.RemoveAllListeners();
        tick.GetComponent<Image>().raycastTarget=false;
        if(view.Completed)
        {
            string historyId=view.IsHistory ? view.history.id : Service.Snapshot.history
                .FirstOrDefault(h=>h.taskId==view.task.id && h.day==view.occurrence.day && !h.revoked)?.id;
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
        string day=view.task.daily && Service.Eligible(view.task) ? TaskService.DateKey(Service.LocalNow) : view.task.date;
        TaskService.TryDate(day,out var date); var time=TimeSpan.FromMinutes(view.task.minuteOfDay);
        string prefix=view.Completed ? "Done today | " : view.task.daily && !Service.Eligible(view.task) ? "Starts " : "";
        return prefix+date.Add(time).ToString("d MMM yyyy h:mm tt",System.Globalization.CultureInfo.InvariantCulture);
    }
    public void SelectTask(string key) { if(!CommitQuickAdd()) return; SelectedKey=key; editing=false; editor.gameObject.SetActive(false); detailsRoot.gameObject.SetActive(true); Refresh(); }
    private void RenderDetails(TaskView view)
    {
        detailsRoot.gameObject.SetActive(!editing && view!=null);
        if(view==null) return;
        details.Render(view, ScheduleText, Service, BeginEdit, CompleteSelected,
            id=>Confirm("Delete task?", "Delete this task? Completed history and rewards will be kept.",
                ()=> { if(!Service.Delete(id)) Notify(Service.LastError,true); else Notify("Task deleted."); }),
            message=>Notify(message,true));
    }
    private static void SpaceSchedule(TMP_Text date)
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
    public void CompleteSelected(string id)
    {
        Action complete=()=>
        {
            int before=Service.Snapshot.fishCoins;
            if(!Service.Complete(id,true)) Notify(Service.LastError,true);
            else Notify(Service.Snapshot.fishCoins>before ? "Task completed. Rewards added!" : "Task completed. Previously earned rewards are kept.");
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
            date=TaskService.DateKey(now),minuteOfDay=now.Hour*60+now.Minute,tagIds=tagFilters.ToList() };
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
            if(suppressQuickBlur || refocusQuick) return;
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
        editor.LoadDraft(PlayerDataStore.Copy(task),Service.Snapshot.tags,done);
        editor.transform.Find("Header/Heading").GetComponent<TMP_Text>().text=title;
        editor.transform.Find("Footer/Actions/SaveChangesButton").GetComponentInChildren<TMP_Text>().text=string.IsNullOrEmpty(task.id) ? "Add Task" : "Save Changes";
    }
    public void SaveDraft()
    {
        var draft=editor.ReadDraft(out var done);
        if(!Service.Upsert(draft,done,out var id)) { Notify(Service.LastError,true); return; }
        SelectedKey=id; CancelDraft(); Refresh(); Notify("Task saved.");
    }
    public void CancelDraft()
    {
        editing=false; editor.datePicker.overlay.SetActive(false); editor.timePicker.overlay.SetActive(false);
        editor.gameObject.SetActive(false);
        if(Service!=null) RenderDetails(visible.FirstOrDefault(v=>v.Key==SelectedKey));
    }
    private void RenderTagGroups(List<QuestTag> tags)
    {
        foreach(var tag in tags) {
            var tasks=visible.Where(view=>view.task.tagIds.Contains(tag.id)).ToList();
            if(tasks.Count==0) continue;
            RenderTagHeading(tag.id,tag.name,tasks.Count);
            foreach(var task in tasks) RenderRow(task);
        }
        var untagged=visible.Where(view=>view.task.tagIds.Count==0).ToList();
        if(untagged.Count>0) {
            RenderTagHeading("untagged","No Tags",untagged.Count);
            foreach(var task in untagged) RenderRow(task);
        }
    }
    private void RenderTagHeading(string id,string caption,int count)
    {
        var row=ui.Row(listContent,"TagGroup_"+id,42,10);
        var source=detailsRoot.Find("ScrollRect/Content/Tags/Title/Icon");
        var icon=Instantiate(source.gameObject,row); icon.SetActive(true);
        var iconSize=TaskUIBuilder.Height(icon.GetComponent<RectTransform>(),26);
        iconSize.minWidth=iconSize.preferredWidth=26; iconSize.flexibleWidth=0;
        foreach(var button in icon.GetComponents<Button>()) { button.onClick.RemoveAllListeners(); button.enabled=false; }
        ui.Text(row,caption+" ("+count+")",22,TaskUIBuilder.Ink);
    }
    private void ShowTagManager()
    {
        OpenModal("Manage Tags",480);
        var content=ui.ScrollContent(modalPanel,"Tags",310);
        foreach(var tag in Service.Snapshot.tags)
        {
            var row=ui.Row(content,"Tag_"+tag.id,40);
            var text=ui.Text(row,tag.name,18); TaskUIBuilder.Flex(text.transform);
            ui.Button(row,"RenameTag","Rename",90).onClick.AddListener(()=>ShowTagEditor(tag));
            ui.Button(row,"DeleteTag","Delete",80,new Color32(255,216,221,255)).onClick.AddListener(()=>
            {
                int affected=Service.Snapshot.tasks.Count(t=>t.tagIds.Contains(tag.id));
                Confirm("Delete tag?","Remove '"+tag.name+"' from "+affected+" tasks? The tasks and completed history will be kept.",()=> { if(!Service.DeleteTag(tag.id)) Notify(Service.LastError,true); else Notify("Tag deleted."); });
            });
        }
        ui.Button(modalPanel,"Close","Close").onClick.AddListener(CloseModal);
    }
    private void ShowTagEditor(QuestTag tag)
    {
        OpenModal(tag==null ? "New Tag" : "Rename Tag",220);
        var input=ui.Input(modalPanel,"Tag name"); input.characterLimit=30; input.text=tag?.name ?? "";
        var row=ui.Row(modalPanel,"Actions",40); ui.Button(row,"Cancel","Cancel").onClick.AddListener(CloseModal);
        ui.Button(row,"Save","Save").onClick.AddListener(()=> { if(!Service.SaveTag(tag?.id,input.text,out _)) Notify(Service.LastError,true); else { CloseModal(); Notify("Tag saved."); } }); input.Select();
    }
    private void ShowFilters()
    {
        OpenModal("Filter Tasks",540);
        var selectedDifficulty=difficultyFilter;
        var radio=modalPanel.gameObject.GetComponent<ToggleGroup>() ?? modalPanel.gameObject.AddComponent<ToggleGroup>(); radio.allowSwitchOff=false;
        var all=ui.Toggle(modalPanel,"Any difficulty",!selectedDifficulty.HasValue);
        all.group=radio;
        var difficultyToggles=new List<Toggle>();
        foreach(TaskDifficulty value in Enum.GetValues(typeof(TaskDifficulty)))
        {
            var toggle=ui.Toggle(modalPanel,TaskService.DifficultyName(value),selectedDifficulty==value); toggle.group=radio; difficultyToggles.Add(toggle);
            toggle.onValueChanged.AddListener(isOn=> { if(!isOn) return; selectedDifficulty=value; all.SetIsOnWithoutNotify(false); foreach(var other in difficultyToggles) if(other!=toggle) other.SetIsOnWithoutNotify(false); });
        }
        all.onValueChanged.AddListener(isOn=> { if(!isOn)return; selectedDifficulty=null; foreach(var toggle in difficultyToggles) toggle.SetIsOnWithoutNotify(false); });
        var selectedTags=new HashSet<string>(tagFilters); var content=ui.ScrollContent(modalPanel,"Tags",190);
        foreach(var tag in Service.Snapshot.tags)
        {
            var toggle=ui.Toggle(content,tag.name,selectedTags.Contains(tag.id)); toggle.onValueChanged.AddListener(isOn=> { if(isOn) selectedTags.Add(tag.id); else selectedTags.Remove(tag.id); });
        }
        var row=ui.Row(modalPanel,"Actions",40); ui.Button(row,"Clear","Clear filters").onClick.AddListener(()=> { difficultyFilter=null; tagFilters.Clear(); CloseModal(); Refresh(); });
        ui.Button(row,"Apply","Apply").onClick.AddListener(()=> { difficultyFilter=selectedDifficulty; tagFilters.Clear(); tagFilters.UnionWith(selectedTags); CloseModal(); Refresh(); });
    }
}
