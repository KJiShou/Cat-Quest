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
        // Bound the detached projection before copying or building views. No Date
        // receipts intentionally remain available regardless of the month browsed.
        string firstKey = DateKey(first), lastKey = DateKey(last);
        var data = CopyTaskSnapshot(store.Data.tasks,
            store.Data.occurrences.Where(o => o.day == "once" || string.CompareOrdinal(o.day, firstKey) >= 0 && string.CompareOrdinal(o.day, lastKey) <= 0),
            showCompleted ? store.Data.history.Where(h => !h.revoked && CalendarReceiptDate(h, out var date)
                && (!date.HasValue || date.Value >= first && date.Value <= last)) : Array.Empty<TaskCompletion>());
        var occurrences = IndexOccurrences(data.occurrences);
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
        TaskView Live(QuestTask task, string day)
        {
            if (!occurrences.TryGetValue((task.id, day), out var occurrence)) occurrence = new TaskOccurrence { taskId = task.id, day = day };
            return LiveTaskView(data, task, occurrence);
        }
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
                DateTime? date = task.scheduled && TryDate(task.date, out var due) ? due : (DateTime?)null;
                if (date.HasValue && (date.Value < first || date.Value > last)) continue;
                var view = Live(task, "once");
                if (!view.Completed) Add(view, CalendarTaskKind.Task, date);
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
            if (!CalendarReceiptDate(receipt, out var date)) continue;
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
    private static bool CalendarReceiptDate(TaskCompletion receipt, out DateTime? date)
    {
        date = null;
        if (receipt.snapshot.daily)
        {
            if (!TryDate(receipt.day, out var day)) return false;
            date = day;
        }
        else if (receipt.snapshot.scheduled && TryDate(receipt.snapshot.date, out var due)) date = due;
        return true;
    }
}
