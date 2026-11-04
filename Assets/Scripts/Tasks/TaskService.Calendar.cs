using System;
using System.Collections.Generic;
using System.Linq;

public enum CalendarTaskKind { Task, History, Preview }

public sealed class CalendarTaskItem
{
    public TaskView view;
    public CalendarTaskKind kind;
    public DateTime? date;
    public string Key => kind + ":" + view.Key + ":" + (date.HasValue ? TaskService.DateKey(date.Value) : "none");
}

public sealed class CalendarTaskDay
{
    public DateTime date;
    public List<CalendarTaskItem> items = new List<CalendarTaskItem>();
    // Missed occurrences have no historical task snapshot. Never infer their old metadata.
    public int missed;
}

public sealed class TaskCalendarResult
{
    public Dictionary<DateTime, CalendarTaskDay> days = new Dictionary<DateTime, CalendarTaskDay>();
    public List<CalendarTaskItem> noDate = new List<CalendarTaskItem>();
}

public sealed partial class TaskService
{
    // Read-only projection: browsing must not create occurrences or write a save.
    public TaskCalendarResult QueryCalendar(DateTime first, DateTime last, string search = "",
        TaskDifficulty? difficulty = null, IEnumerable<string> tagIds = null, bool showCompleted = true)
    {
        first = first.Date; last = last.Date;
        if (last < first || (last - first).TotalDays > 366) throw new ArgumentException("Calendar range must be at most one year.");
        var data = Snapshot;
        var today = LocalNow.Date;
        var selectedTags = (tagIds ?? Array.Empty<string>()).ToArray();
        string text = (search ?? "").Trim();
        var result = new TaskCalendarResult();
        for (var date = first; ; date = date.AddDays(1))
        {
            result.days.Add(date, new CalendarTaskDay { date = date });
            if (date == last) break;
        }
        bool Matches(TaskView view) => (!difficulty.HasValue || view.task.difficulty == difficulty.Value)
            && selectedTags.All(id => view.task.tagIds.Contains(id))
            && (text.Length == 0 || ((view.task.title ?? "") + " " + view.task.description + " " + string.Join(" ", view.tagNames))
                .IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        TaskView Live(QuestTask task, string day) => new TaskView {
            task = task,
            occurrence = data.occurrences.FirstOrDefault(o => o.taskId == task.id && o.day == day)
                ?? new TaskOccurrence { taskId = task.id, day = day },
            tagNames = data.tags.Where(t => task.tagIds.Contains(t.id)).Select(t => t.name).ToList(),
            tagColors = data.tags.Where(t => task.tagIds.Contains(t.id)).Select(t => t.colorHex).ToList()
        };
        void Add(TaskView view, CalendarTaskKind kind, DateTime? date)
        {
            if (!Matches(view) || (!showCompleted && view.Completed)) return;
            var item = new CalendarTaskItem { view = view, kind = kind, date = date };
            if (!date.HasValue) result.noDate.Add(item);
            else if (result.days.TryGetValue(date.Value, out var bucket)) bucket.items.Add(item);
        }
        foreach (var task in data.tasks)
        {
            if (!task.daily)
            {
                var view = Live(task, "once");
                if (!view.Completed) Add(view, CalendarTaskKind.Task, task.scheduled && TryDate(task.date, out var due) ? due : (DateTime?)null);
                continue;
            }
            foreach (var date in result.days.Keys.Where(d => d >= today && TaskRepeat.Matches(task, d)))
            {
                var view = Live(task, DateKey(date));
                // Completed rows use the immutable receipt below, including today's Daily.
                if (date == today && view.Completed) continue;
                Add(view, date == today ? CalendarTaskKind.Task : CalendarTaskKind.Preview, date);
            }
        }
        if (showCompleted)
        foreach (var receipt in data.history.Where(h => !h.revoked))
        {
            var task = receipt.snapshot;
            DateTime? date = null;
            if (task.daily) { if (!TryDate(receipt.day, out var day)) continue; date = day; }
            else if (task.scheduled && TryDate(task.date, out var due)) date = due;
            Add(new TaskView { task = task, history = receipt, tagNames = receipt.tagNames, tagColors = receipt.tagColors,
                occurrence = new TaskOccurrence { taskId = receipt.taskId, day = receipt.day, completed = true,
                    rewardGranted = true, completedSubtaskIds = task.subtasks.Select(s => s.id).ToList() } }, CalendarTaskKind.History, date);
        }
        // With metadata filters active we cannot honestly attribute old unsnapshotted occurrences.
        if (text.Length == 0 && !difficulty.HasValue && selectedTags.Length == 0)
        foreach (var occurrence in data.occurrences.Where(o => !o.completed && o.day != "once"))
            if (TryDate(occurrence.day, out var day) && day < today && result.days.TryGetValue(day, out var bucket)) bucket.missed++;
        foreach (var bucket in result.days.Values)
            bucket.items = bucket.items.OrderBy(i => i.view.task.daily).ThenBy(i => i.view.Completed).ThenBy(i => i.view.task.title, StringComparer.OrdinalIgnoreCase).ToList();
        result.noDate = result.noDate.OrderBy(i => i.view.Completed).ThenBy(i => i.view.task.title, StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }
}
