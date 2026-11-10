using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DashboardTasksUI : MonoBehaviour
{
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

    private TaskService service;
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

    private void RequestComplete(TaskView view,RectTransform origin)
    {
        if (view.Completed)
            return;

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

    private void CompleteTask(string id,RectTransform origin)
    {
        int experience=service.Experience,coins=service.FishCoins,tickets=service.Tickets;
        if(!rewardFlight)rewardFlight=GetComponentInParent<Canvas>().rootCanvas.GetComponentInChildren<RewardFlightUI>(true);
        rewardFlight?.SetRewardOrigin(origin);
        bool saved;
        try {saved=service.Complete(id,true);}finally {rewardFlight?.SetRewardOrigin(null);}
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
        service = GameDataManager.EnsureInstance().Tasks;
        if (rows == null) {
            TaskUIBuilder.Clear(taskScroll.content);
            rows = new TaskListViewport(taskScroll.content);
        }
        ui = new TaskUIBuilder(
            taskManager.font,
            taskManager.roundedSprite);

        service.RefreshDate();
        service.Changed += Refresh;
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
            service.Changed -= Refresh;
        confirmation?.Dismiss();
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
        // Finish the outside click before rebuilding the rows under the pointer.
        if (savingTask) { pendingRefresh = true; return; }
        pendingRefresh = false;
        var content = taskScroll.content;
        rows.Begin();
        var snapshot = service.Snapshot;

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

    public bool PrepareToLeavePage()
    {
        if (!quickAddRoot || !quickAddRoot.activeSelf) return true;
        // An unsuccessful blur may already have opened the error dialog this click.
        if (confirmation != null && confirmation.Root.activeSelf) return false;
        if (string.IsNullOrWhiteSpace(quickAddInput.text))
        {
            CancelAddTask();
            return true;
        }
        return CommitAddTask();
    }

    public bool CommitAddTask()
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
        try { saved = service.Upsert(draft, System.Array.Empty<string>(), out _); }
        finally { savingTask = false; }
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
        return true;
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
}
