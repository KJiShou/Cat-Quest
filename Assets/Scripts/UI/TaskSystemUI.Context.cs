using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public partial class TaskSystemUI
{
    TaskContextMenu contextMenu;
    Coroutine contextFocus;
    bool ContextOpen => contextMenu && contextMenu.gameObject.activeSelf;

    public void BindTaskContext(GameObject target, TaskView view, CalendarTaskItem calendarItem = null)
    {
        // History keeps its existing read-only details and eligible undo checkbox.
        TaskContextTarget.Bind(target, view.IsHistory ? null : point => OpenTaskContext(view.task.id, point, calendarItem));
    }
    void OpenTaskContext(string id, Vector2 point, CalendarTaskItem item)
    {
        if (editing || TutorialOwnsInput || HasTaskModal || !CommitQuickAdd()) return;
        var view = CurrentContextTask(id);
        if (view == null) return;
        if (!EnsureTaskContextMenu()) return;
        int version = Service.ReadVersion;
        string day = TaskService.DateKey(Service.LocalNow);
        bool canComplete = !view.Completed && Service.Eligible(view.task)
            && (item == null || item.kind == CalendarTaskKind.Task);
        contextMenu.ShowAsync(view.task, Service.TagCatalog, Service.LocalNow, canComplete, point, choice =>
        {
            var current = CurrentContextTask(id);
            if (current == null || version != Service.ReadVersion || day != TaskService.DateKey(Service.LocalNow))
            { Notify("This task changed. Reopen it to continue.", true); return; }
            if (choice == TaskContextAction.Complete)
            {
                if (canComplete) CompleteForDate(id, current.task.daily ? day : null);
                return;
            }
            if (choice == TaskContextAction.Delete) { CalendarDelete(id); return; }
            if (item != null)
            {
                calendar.OpenItem(item);
                CalendarEdit(item);
            }
            else { SelectedKey = id; BeginEdit(current); }
            if (editing) contextFocus = StartCoroutine(FocusContextField());
        }, async change =>
        {
            var current = CurrentContextTask(id);
            if (current == null || version != Service.ReadVersion || day != TaskService.DateKey(Service.LocalNow))
                return "This task changed. Reopen the menu to continue.";
            var draft = PlayerDataStore.Copy(current.task);
            if (!change.ApplyTo(draft)) return "Choose a field to change.";
            // Reuse current progress, including when editing a future Daily preview.
            if (!(await Service.UpsertAsync(draft, current.occurrence.completedSubtaskIds)).Succeeded)
            {
                string error = Service.LastError;
                return error != null && error.StartsWith("Could not save your changes.", System.StringComparison.Ordinal)
                    ? "Could not save. Your choices are kept. Please try again."
                    : error ?? "Could not save. Please try again.";
            }
            if(this && isActiveAndEnabled)Notify("Task updated.");
            return null;
        });
    }
    bool EnsureTaskContextMenu()
    {
        if (contextMenu) return true;
        var template = Resources.Load<TaskContextMenu>("TaskContextMenu");
        if (!template) { Notify("Task menu is unavailable.", true); return false; }
        contextMenu = Instantiate(template, GetComponentInParent<Canvas>().rootCanvas.transform);
        return true;
    }
    void OpenQuickAddContext(Vector2 point)
    {
        if (!quickInput || committingQuick || editing || TutorialOwnsInput || HasTaskModal || !EnsureTaskContextMenu()) return;
        pendingQuickBlur = false;
        refocusQuick = false;
        quickDraft.title = quickInput.text.Trim();
        contextMenu.Show(quickDraft, Service.TagCatalog, Service.LocalNow, false, point,
            _ => EditQuickAddInfo(), change =>
            {
                if (!change.ApplyTo(quickDraft)) return "Choose a field to change.";
                RefreshQuickSchedule();
                return null;
            }, () => refocusQuick = quickInput && isActiveAndEnabled);
    }
    void EditQuickAddInfo()
    {
        if (!quickInput || committingQuick) return;
        var draft = quickDraft;
        draft.title = quickInput.text.Trim();
        CancelQuickAdd();
        OpenQuickAddDraft(draft);
    }
    internal void OpenQuickAddDraft(QuestTask draft)
    {
        if (calendar && calendar.Active) calendar.SetMode(TaskDisplayMode.List);
        OpenDraft(draft, Array.Empty<string>(), "Add Task");
        if (draft.title.Length > 0) savedDraftState = null;
        contextFocus = StartCoroutine(FocusContextField());
    }
    TaskView CurrentContextTask(string id) => Service.Query(TaskTab.Todo).Concat(Service.Query(TaskTab.Daily))
        .FirstOrDefault(v => v.task.id == id && (!v.Completed || v.task.daily));
    IEnumerator FocusContextField()
    {
        // Let the original editor's layout settle before positioning a dropdown/picker.
        yield return null;
        contextFocus = null;
        if (!editing || !isActiveAndEnabled) yield break;
        Canvas.ForceUpdateCanvases();
        editor.taskTitle.Select(); editor.taskTitle.ActivateInputField();
    }
    void CloseTaskContext()
    {
        if (contextMenu) contextMenu.Dismiss();
    }
    void StopContextFocus()
    {
        if (contextFocus != null) { StopCoroutine(contextFocus); contextFocus = null; }
    }
}
