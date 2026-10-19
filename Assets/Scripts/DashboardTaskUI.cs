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

    private TaskService service;
    private TaskUIBuilder ui;

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

    private void RequestComplete(TaskView view)
    {
        if (view.Completed)
            return;

        if (service.HasIncompleteSubtasks(view.task.id))
        {
            GetConfirmation().Show(
                "Complete remaining subtasks?",
                "Some subtasks are unfinished. Complete them all and collect rewards?",
                () => CompleteTask(view.task.id));
        }
        else
        {
            CompleteTask(view.task.id);
        }
    }

    private void CompleteTask(string id)
    {
        if (!service.Complete(id, true))
        {
            GetConfirmation().Show(
                "Could not complete task",
                service.LastError,
                () => { });
        }
    }

    private void OnDestroy()
    {
        if (confirmation != null && confirmation.Root)
            Destroy(confirmation.Root);
    }

    private void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        ui = new TaskUIBuilder(
            taskManager.font,
            taskManager.roundedSprite);

        service.RefreshDate();
        service.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (service != null)
            service.Changed -= Refresh;
        confirmation?.Dismiss();
    }

    public void Refresh()
    {
        var content = taskScroll.content;
        TaskUIBuilder.Clear(content);

        var daily = service.Query(TaskTab.Daily)
            .Where(view => service.Eligible(view.task))
            .ToArray();

        var today = service.Query(TaskTab.Today)
            .Where(view => !view.task.daily)
            .ToArray();

        RenderGroup(content, "  Daily Tasks", daily,
            "  No daily tasks yet.");

        RenderGroup(content, "  Today's Tasks", today,
            "  No tasks due today.");

        LayoutRebuilder.MarkLayoutForRebuild(content);
    }

    private void RenderGroup(
        RectTransform content,
        string heading,
        TaskView[] tasks,
        string emptyMessage)
    {
        var title = ui.Text(content, heading, 22);
        title.fontStyle = FontStyles.Bold;
        title.color = new Color32(62, 120, 168, 255);
        TaskUIBuilder.Height(title.rectTransform, 38);

        foreach (var task in tasks)
        {
            var row = Instantiate(rowPrefab, content);
            row.gameObject.SetActive(true);

            row.Bind(
                task,
                taskManager.editor.difficultySprites,
                OpenTask,
                RequestComplete,
                service.PreviewReward(task));
        }

        if (tasks.Length == 0)
        {
            ui.Text(content, emptyMessage, 18,
                new Color32(121, 143, 173, 255));
        }
    }

    private void OpenTask(TaskView view)
    {
        if (navigation.ShowPage(MainMenuPage.Tasks))
            navigation.StartCoroutine(SelectTaskWhenReady(view));
    }

    private IEnumerator SelectTaskWhenReady(TaskView view)
    {
        yield return new WaitUntil(() => taskManager.Service != null);

        if (navigation.CurrentPage != MainMenuPage.Tasks)
            yield break;

        taskManager.SelectTab(
            view.task.daily ? TaskTab.Daily : TaskTab.Today);

        taskManager.SelectTask(view.task.id);
    }
}
