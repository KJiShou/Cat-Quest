using System;

// Presentation buckets: the first matching, most specific date range wins.
public enum TaskDateGroup { Due, Daily, Today, Tomorrow, ThisWeek, NextWeek, ThisMonth, NextMonth, Later, NoDate }

public static class TaskDateGroups
{
    public static TaskDateGroup Classify(QuestTask task, DateTime now)
    {
        if (task.daily) return TaskDateGroup.Daily;
        if (!task.scheduled || !TaskService.TryDate(task.date, out var date)) return TaskDateGroup.NoDate;
        var today = now.Date;
        if (date < today) return TaskDateGroup.Due;
        if (date == today) return TaskDateGroup.Today;
        if (date == today.AddDays(1)) return TaskDateGroup.Tomorrow;
        var nextMonday = today.AddDays(7 - ((int)today.DayOfWeek + 6) % 7);
        if (date < nextMonday) return TaskDateGroup.ThisWeek;
        if (date < nextMonday.AddDays(7)) return TaskDateGroup.NextWeek;
        var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
        if (date < nextMonth) return TaskDateGroup.ThisMonth;
        if (date < nextMonth.AddMonths(1)) return TaskDateGroup.NextMonth;
        return TaskDateGroup.Later;
    }

    public static string Caption(TaskDateGroup group)
    {
        switch (group)
        {
            case TaskDateGroup.Due: return "Due";
            case TaskDateGroup.Daily: return "Daily";
            case TaskDateGroup.Today: return "Today";
            case TaskDateGroup.Tomorrow: return "Tomorrow";
            case TaskDateGroup.ThisWeek: return "This Week";
            case TaskDateGroup.NextWeek: return "Next Week";
            case TaskDateGroup.ThisMonth: return "This Month";
            case TaskDateGroup.NextMonth: return "Next Month";
            case TaskDateGroup.Later: return "Later";
            default: return "No Date";
        }
    }
}
