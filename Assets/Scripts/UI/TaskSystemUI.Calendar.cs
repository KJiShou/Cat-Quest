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
    public void CalendarDelete(string id) => ConfirmDeleteTask(id);
    void ConfirmDeleteTask(string id)
    {
        var task=Service.Snapshot.tasks.FirstOrDefault(t=>t.id==id);
        if(task==null) { Notify("This task no longer exists. Reopen the list to continue.",true); return; }
        // Bound only the name so the consequence remains visible in the authored popup.
        var name=new System.Globalization.StringInfo(task.title);
        string caption=name.LengthInTextElements>40?name.SubstringByTextElements(0,40)+"…":task.title;
        string body="\""+caption+"\"\n"+(task.daily?"This stops this task and all future repeats.\n":"")
            +"Completed history and rewards are kept.";
        Confirm(task.daily?"Delete repeating task?":"Delete task?",body,
            () => { if (!Service.Delete(id)) Notify(Service.LastError, true); else Notify("Task deleted."); },"Delete");
    }
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
