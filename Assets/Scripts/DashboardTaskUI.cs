using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DashboardTasksUI : MonoBehaviour
{
    int observedRewardLevel = -1;
    void RefreshRecords(System.Collections.Generic.IReadOnlyCollection<PlayerRecordKind> changed)
    {
        bool levelChanged=observedRewardLevel!=service.RewardLevel;
        observedRewardLevel=service.RewardLevel;
        if(changed==null || levelChanged || changed.Contains(PlayerRecordKind.Tasks)
            || changed.Contains(PlayerRecordKind.Occurrences) || changed.Contains(PlayerRecordKind.History)
            || changed.Contains(PlayerRecordKind.Tags) || changed.Contains(PlayerRecordKind.RewardDays)) Refresh();
    }
    public ScrollRect taskScroll;
    public DashboardTaskRow rowPrefab;
    public MainMenuNavigation navigation;
    public TaskSystemUI taskManager;
    public Button addTaskButton;
    public GameObject quickAddRoot;
    public TMP_InputField quickAddInput;
    private bool savingTask;
    private bool pendingRefresh;
    private bool pendingBlur;
    private TaskResultNotice resultNotice;
    private RewardFlightUI rewardFlight;

    private SqlitePlayerService service;
    private TaskUIBuilder ui;
    private TaskListViewport rows;

    private TaskConfirmDialog confirmation;

    private TaskConfirmDialog GetConfirmation()
    {
        if (confirmation == null)
        {
            var canvas = GetComponentInParent<Canvas>().rootCanvas;

            confirmation = new TaskConfirmDialog(
                taskManager.confirmDialogTemplate,
                canvas.transform,
                taskManager.font);

            confirmation.Root.name = "DashboardTaskConfirmDialog";
        }

        return confirmation;
    }

    private async void RequestComplete(TaskView view,RectTransform origin)
    {
        if (!PrepareToLeavePage()) return;
        if (view.Completed) {
            string receipt = service.CompletionId(view.task.id, view.occurrence.day);
            bool saved=(await service.UndoCompletionAsync(receipt)).Succeeded;
            if(!this || !isActiveAndEnabled)return;
            if (!saved) ShowTaskNotice(service.LastError, true);
            else ShowTaskNotice("Task restored. Previously earned rewards are kept.");
            return;
        }

        if (service.HasIncompleteSubtasks(view.task.id))
        {
            GetConfirmation().Show(
                "Complete remaining subtasks?",
                "Some subtasks are unfinished. Complete them all and collect rewards?",
                () => CompleteTask(view.task.id,origin));
        }
        else
        {
            CompleteTask(view.task.id,origin);
        }
    }

    private readonly System.Collections.Generic.HashSet<string> pendingCompletions = new System.Collections.Generic.HashSet<string>();
    private async void CompleteTask(string id,RectTransform origin)
    {
        if(!pendingCompletions.Add(id))return;
        int experience=service.Experience,coins=service.FishCoins,tickets=service.Tickets;
        if(!rewardFlight)rewardFlight=GetComponentInParent<Canvas>().rootCanvas.GetComponentInChildren<RewardFlightUI>(true);
        rewardFlight?.SetRewardOrigin(origin);
        bool saved;
        try {saved=(await service.CompleteAsync(id,true)).Succeeded;}finally {pendingCompletions.Remove(id);rewardFlight?.SetRewardOrigin(null);}
        if (!this || !isActiveAndEnabled) return;
        if (!saved)
        {
            GetConfirmation().Show(
                "Could not complete task",
                service.LastError,
                () => { });
        }
        else {
            SettingsAudio.Instance?.PlayTaskComplete();
            if(experience==service.Experience&&coins==service.FishCoins&&tickets==service.Tickets) {
                if(!resultNotice)resultNotice=Instantiate(Resources.Load<TaskResultNotice>("TaskResultNotice"),GetComponentInParent<Canvas>().rootCanvas.transform);
                resultNotice.Show("Task completed. No additional rewards; daily limits and previously earned rewards are unchanged.");
            }
        }
    }

    private void OnDestroy()
    {
        if (contextMenu) Destroy(contextMenu.gameObject);
        if (resultNotice) Destroy(resultNotice.gameObject);
        if (confirmation != null && confirmation.Root)
            Destroy(confirmation.Root);
    }

    private void OnEnable()
    {
        if (addTaskButton) addTaskButton.onClick.AddListener(BeginAddTask);
        if (quickAddInput)
        {
            quickAddInput.onSubmit.AddListener(SubmitTask);
            quickAddInput.onEndEdit.AddListener(EndTaskInput);
            quickAddInput.onDeselect.AddListener(LeaveTaskInput);
        }
        service = GameDataManager.EnsureInstance().Service;
        if (rows == null) {
            TaskUIBuilder.Clear(taskScroll.content);
            rows = new TaskListViewport(taskScroll.content);
        }
        ui = new TaskUIBuilder(
            taskManager.font,
            taskManager.roundedSprite);

        _ = service.RefreshDateAsync();
        service.RecordsChanged += RefreshRecords;
        Refresh();
    }

    private void OnDisable()
    {
        pendingBlur = false;
        if (addTaskButton) addTaskButton.onClick.RemoveListener(BeginAddTask);
        if (quickAddInput)
        {
            quickAddInput.onSubmit.RemoveListener(SubmitTask);
            quickAddInput.onEndEdit.RemoveListener(EndTaskInput);
            quickAddInput.onDeselect.RemoveListener(LeaveTaskInput);
        }
        if (resultNotice) resultNotice.Dismiss();
        if (service != null)
            service.RecordsChanged -= RefreshRecords;
        confirmation?.Dismiss();
        if (contextMenu) contextMenu.Dismiss();
    }

    private void LateUpdate()
    {
        if (pendingBlur)
        {
            pendingBlur = false;
            if (!quickAddInput.isFocused) PrepareToLeavePage();
        }
        if (pendingRefresh) Refresh();
        rows?.Update();
    }

    public void Refresh()
    {
        if (contextMenu) contextMenu.Dismiss();
        // Finish the outside click before rebuilding the rows under the pointer.
        if (savingTask) { pendingRefresh = true; return; }
        pendingRefresh = false;
        var content = taskScroll.content;
        rows.Begin();
        observedRewardLevel=service.RewardLevel;
        var snapshot = service.TaskSnapshot;

        var daily = service.QuerySnapshot(snapshot, TaskTab.Daily)
            .Where(view => service.Eligible(view.task))
            .ToArray();

        var today = service.QuerySnapshot(snapshot, TaskTab.Today)
            .Where(view => !view.task.daily)
            .ToArray();

        RenderGroup(content, "Daily Tasks", daily,
            "No daily tasks yet.");

        RenderGroup(content, "Today's Tasks", today,
            "No tasks due today.");

        var upcoming = service.QuerySnapshot(snapshot, TaskTab.Todo)
            .Where(view => !view.task.scheduled || (TaskService.TryDate(view.task.date, out var date) && date.Date > service.LocalNow.Date));
        foreach (var group in upcoming.GroupBy(view => TaskDateGroups.Classify(view.task, service.LocalNow)).OrderBy(group => group.Key))
            RenderGroup(content, TaskDateGroups.Caption(group.Key), group.ToArray(), "");

        rows.Update(true);
        LayoutRebuilder.MarkLayoutForRebuild(content);
    }

    private void RenderGroup(
        RectTransform content,
        string heading,
        TaskView[] tasks,
        string emptyMessage)
    {
        rows.Add("heading/"+heading, "heading", 38, () => {
            var title=ui.Text(content, "", 22);
            title.fontStyle=FontStyles.Bold; title.color=new Color32(62,120,168,255);
            title.margin=new Vector4(12,0,0,0); TaskUIBuilder.Height(title.rectTransform,38);
            return title.gameObject;
        }, go=>go.GetComponent<TMP_Text>().text=heading);
        var rect=(RectTransform)rowPrefab.transform;
        float height=LayoutUtility.GetPreferredHeight(rect);
        if (height<=0) height=rect.rect.height;
        foreach (var task in tasks) {
            rows.Add("task/"+task.Key, "task", height, ()=>Instantiate(rowPrefab,content).gameObject,
                go=>{
                    var row=go.GetComponent<DashboardTaskRow>();
                    row.Bind(task,taskManager.editor.difficultySprites,OpenTask,
                        view=>RequestComplete(view,row.completeButton.transform as RectTransform),service.PreviewReward(task));
                    row.completeButton.interactable = task.Completed
                        ? service.CanUndoCompletion(service.CompletionId(task.task.id, task.occurrence.day))
                        : service.Eligible(task.task);
                    TaskContextTarget.Bind(row.gameObject, point => OpenTaskContext(task, point));
                    TaskContextTarget.Bind(row.completeButton.gameObject, point => OpenTaskContext(task, point));
                });
        }
        if (tasks.Length == 0) {
            rows.Add("empty/"+heading, "empty", 26, ()=>ui.Text(content,"",18,new Color32(121,143,173,255)).gameObject,
                go=>{ var label=go.GetComponent<TMP_Text>(); label.text=emptyMessage; label.margin=new Vector4(12,0,0,0); });
        }
    }

    private void OpenTask(TaskView view)
    {
        if (navigation.ShowPage(MainMenuPage.Tasks))
            navigation.StartCoroutine(SelectTaskWhenReady(view));
    }

    public void BeginAddTask()
    {
        if (!isActiveAndEnabled || !quickAddRoot || !quickAddInput) return;
        addTaskButton.gameObject.SetActive(false);
        quickAddRoot.SetActive(true);
        TaskUIBuilder.BlueCaret(quickAddInput);
        quickAddInput.Select();
        quickAddInput.ActivateInputField();
    }

    private void SubmitTask(string _) => CommitAddTask();
    private void EndTaskInput(string _)
    {
        if (quickAddInput.wasCanceled) CancelAddTask();
    }

    private void LeaveTaskInput(string _)
    {
        if (!isActiveAndEnabled || !quickAddRoot.activeSelf || savingTask) return;
        if (quickAddInput.wasCanceled) CancelAddTask();
        // TMP may be inside EventSystem selection dispatch. Defer so an error
        // popup can safely take focus, and the clicked control handles its action.
        else pendingBlur = true;
    }

    public bool PrepareToLeavePage(System.Action afterSaved = null)
    {
        if (!quickAddRoot || !quickAddRoot.activeSelf) return true;
        // An unsuccessful blur may already have opened the error dialog this click.
        if (confirmation != null && confirmation.Root.activeSelf) return false;
        if (string.IsNullOrWhiteSpace(quickAddInput.text))
        {
            CancelAddTask();
            return true;
        }
        return CommitAddTask(afterSaved);
    }

    public bool CommitAddTask(System.Action afterSaved = null)
    {
        if (!isActiveAndEnabled || !quickAddRoot.activeSelf || savingTask || string.IsNullOrWhiteSpace(quickAddInput.text)) return false;
        _ = CommitAddTaskAsync(afterSaved); return false;
    }
    private async System.Threading.Tasks.Task<bool> CommitAddTaskAsync(System.Action afterSaved)
    {
        if (!isActiveAndEnabled || !quickAddRoot.activeSelf || savingTask) return false;
        if (string.IsNullOrWhiteSpace(quickAddInput.text)) return false;
        var draft = new QuestTask
        {
            title = quickAddInput.text.Trim(),
            difficulty = TaskDifficulty.Normal,
            scheduled = true,
            date = TaskService.DateKey(service.LocalNow)
        };
        savingTask = true;
        bool saved;
        try { saved = (await service.UpsertAsync(draft, System.Array.Empty<string>())).Succeeded; }
        finally { savingTask = false; }
        if (!this || !isActiveAndEnabled || !quickAddRoot.activeSelf) return saved;
        if (!saved)
        {
            GetConfirmation().ShowNotice("Could not add task", service.LastError);
            return false;
        }
        CancelAddTask();
        if (!resultNotice)
            resultNotice = Instantiate(Resources.Load<TaskResultNotice>("TaskResultNotice"),
                GetComponentInParent<Canvas>().rootCanvas.transform);
        resultNotice.Show("Task saved.");
        afterSaved?.Invoke(); return true;
    }

    public void CancelAddTask()
    {
        pendingBlur = false;
        // Hide before clearing focus so TMP end-edit cannot submit the same draft.
        quickAddRoot.SetActive(false);
        quickAddInput.SetTextWithoutNotify("");
        addTaskButton.gameObject.SetActive(true);
    }

    private IEnumerator SelectTaskWhenReady(TaskView view)
    {
        yield return new WaitUntil(() => taskManager.Service != null);

        if (navigation.CurrentPage != MainMenuPage.Tasks)
            yield break;

        taskManager.SelectTab(
            view.task.daily ? TaskTab.Daily : TaskTab.Todo);

        taskManager.SelectTask(view.task.id);
    }

    private TaskContextMenu contextMenu;

    private void ShowTaskNotice(string message, bool error = false)
    {
        if (error) { GetConfirmation().ShowNotice("Could not update task", message); return; }
        if (!resultNotice)
            resultNotice = Instantiate(Resources.Load<TaskResultNotice>("TaskResultNotice"),
                GetComponentInParent<Canvas>().rootCanvas.transform);
        resultNotice.Show(message);
    }

    private TaskView CurrentContextTask(string id) => service.Query(TaskTab.Daily)
        .Concat(service.Query(TaskTab.Todo)).FirstOrDefault(v => v.task.id == id);

    private void OpenTaskContext(TaskView view, Vector2 point)
    {
        if (!PrepareToLeavePage()) return;
        var current = CurrentContextTask(view.task.id);
        if (current == null) return;
        if (!contextMenu)
            contextMenu = Instantiate(Resources.Load<TaskContextMenu>("TaskContextMenu"),
                GetComponentInParent<Canvas>().rootCanvas.transform);
        string id = current.task.id;
        int version = service.ReadVersion;
        string day = TaskService.DateKey(service.LocalNow);
        bool IsCurrent() => isActiveAndEnabled && version == service.ReadVersion
            && day == TaskService.DateKey(service.LocalNow) && CurrentContextTask(id) != null;
        contextMenu.ShowAsync(current.task, service.TagCatalog, service.LocalNow,
            !current.Completed && service.Eligible(current.task), point, action =>
        {
            if (!IsCurrent()) { ShowTaskNotice("This task changed. Reopen it to continue.", true); return; }
            if (action == TaskContextAction.Complete) RequestComplete(current, null);
            else if (action == TaskContextAction.Delete)
            {
                var name = new System.Globalization.StringInfo(current.task.title);
                string caption = name.LengthInTextElements > 40 ? name.SubstringByTextElements(0, 40) + "…" : current.task.title;
                GetConfirmation().Show(current.task.daily ? "Delete repeating task?" : "Delete task?",
                    "\"" + caption + "\"\n" + (current.task.daily ? "This stops this task and all future repeats.\n" : "")
                    + "Completed history and rewards are kept.", async () =>
                {
                    if (!IsCurrent()) { ShowTaskNotice("This task changed. Reopen it to continue.", true); return; }
                    var saved=await service.DeleteAsync(id);
                    if(!this || !isActiveAndEnabled)return;
                    if (!saved.Succeeded) ShowTaskNotice(service.LastError, true);
                    else ShowTaskNotice("Task deleted.");
                }, "Delete");
            }
            else if (navigation.ShowPage(MainMenuPage.Tasks))
                navigation.StartCoroutine(EditTaskWhenReady(id));
        }, async change =>
        {
            if (!IsCurrent()) return "This task changed. Reopen the menu to continue.";
            var latest = CurrentContextTask(id);
            var draft = PlayerDataStore.Copy(latest.task);
            if (change.field == TaskContextAction.Difficulty) draft.difficulty = change.difficulty;
            else if (change.field == TaskContextAction.Tags) draft.tagIds = change.tagIds;
            else if (change.field == TaskContextAction.ChooseDate)
            {
                draft.scheduled = change.scheduled;
                if (change.scheduled) draft.date = TaskService.DateKey(change.date);
                else draft.daily = false;
            }
            else return "Choose a field to change.";
            if (!(await service.UpsertAsync(draft, latest.occurrence.completedSubtaskIds)).Succeeded)
                return service.LastError ?? "Could not save. Please try again.";
            if(this && isActiveAndEnabled)ShowTaskNotice("Task updated.");
            return null;
        });
    }

    private System.Collections.IEnumerator EditTaskWhenReady(string id)
    {
        yield return new WaitUntil(() => taskManager.Service != null);
        if (navigation.CurrentPage != MainMenuPage.Tasks) yield break;
        var current = CurrentContextTask(id);
        if (current == null) yield break;
        taskManager.SelectTab(current.task.daily ? TaskTab.Daily : TaskTab.Todo);
        taskManager.SelectTask(id);
        taskManager.CalendarDraft(current.task, current.occurrence.completedSubtaskIds);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && contextMenu) contextMenu.Dismiss();
        if (focused && isActiveAndEnabled && service != null) { _ = service.RefreshDateAsync(); Refresh(); }
    }
}
