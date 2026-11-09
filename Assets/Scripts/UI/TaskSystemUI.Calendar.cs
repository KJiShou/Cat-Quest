using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum TaskDisplayMode { List, Calendar }

public partial class TaskSystemUI
{
    public TaskCalendarUI calendar;
    public string CalendarSchedule(TaskView view) => ScheduleText(view);
    public bool IsEditing => editing;
    public bool HasTaskModal => ContextOpen || (confirmation != null && confirmation.Root.activeInHierarchy) || (filters && filters.gameObject.activeInHierarchy);
    public void ShowCalendarFilters(TaskDifficulty? difficulty, IEnumerable<string> tags, Action<TaskDifficulty?, HashSet<string>> apply)
        => filters.Show(difficulty, tags, Service.TagCatalog, apply);
    public void CalendarDraft(QuestTask task, IEnumerable<string> done, bool preview = false)
    {
        OpenDraft(task, done, preview ? "Edit daily task" : string.IsNullOrEmpty(task.id) ? "Add Task" : "Edit Task");
    }
    public void CalendarDelete(string id) => Confirm("Delete task?", "Completed history and rewards will be kept.",
        () => { if (!Service.Delete(id)) Notify(Service.LastError, true); else Notify("Task deleted."); }, "Delete");
    public void CalendarUndo(string historyId)
    {
        if (!Service.UndoCompletion(historyId)) Notify(Service.LastError, true);
        else Notify("Task restored. Previously earned rewards are kept.");
    }
    public void CalendarEdit(CalendarTaskItem item)
    {
        // Preview edits target the current template; never overwrite today's subtask progress with a future preview.
        var current = Service.Query(item.view.task.daily ? TaskTab.Daily : TaskTab.Todo).FirstOrDefault(v => v.task.id == item.view.task.id);
        if (current == null || (current.Completed && !current.task.daily)) { Notify("This task changed. Reopen it to continue.", true); return; }
        calendar.StartEdit();
        CalendarDraft(current.task, current.occurrence.completedSubtaskIds, item.kind == CalendarTaskKind.Preview);
    }
}
