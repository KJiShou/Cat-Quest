using System;
using System.Globalization;

// Zero-valued fields on older daily tasks mean every day.
public enum RepeatUnit { Days, Weeks, Months, Years, Weekdays, MonthlyWeekday }

public static class TaskRepeat
{
    public static bool ValidRule(QuestTask task) =>
        Enum.IsDefined(typeof(RepeatUnit), task.repeatUnit) && task.repeatInterval >= 0 && task.repeatInterval <= 999
        && task.repeatWeekdays >= 0 && task.repeatWeekdays <= 127
        && (string.IsNullOrEmpty(task.repeatUntil) || (TaskService.TryDate(task.repeatUntil, out var end)
            && TaskService.TryDate(task.date, out var start) && end >= start));

    public static bool Matches(QuestTask task, DateTime date)
    {
        if (!TaskService.TryDate(task.date, out var start) || date.Date < start) return false;
        if (!string.IsNullOrEmpty(task.repeatUntil) && (!TaskService.TryDate(task.repeatUntil, out var until) || date.Date > until)) return false;
        int interval = Math.Max(1, task.repeatInterval);
        int days = (date.Date - start).Days;
        int months = (date.Year - start.Year) * 12 + date.Month - start.Month;
        switch (task.repeatUnit)
        {
            case RepeatUnit.Days: return days % interval == 0;
            case RepeatUnit.Weeks:
                int startWeekday = ((int)start.DayOfWeek + 6) % 7;
                int weekday = ((int)date.DayOfWeek + 6) % 7;
                int mask = task.repeatWeekdays == 0 ? 1 << startWeekday : task.repeatWeekdays;
                return (days + startWeekday) / 7 % interval == 0 && (mask & (1 << weekday)) != 0;
            case RepeatUnit.Months: return months % interval == 0 && date.Day == start.Day;
            case RepeatUnit.MonthlyWeekday: return months % interval == 0 && date.DayOfWeek == start.DayOfWeek && (date.Day - 1) / 7 == (start.Day - 1) / 7;
            case RepeatUnit.Years: return (date.Year - start.Year) % interval == 0 && date.Month == start.Month && date.Day == start.Day;
            case RepeatUnit.Weekdays: return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
            default: return false;
        }
    }

    // Jump to the next applicable interval, then examine only dates within that interval.
    public static DateTime? Next(QuestTask task, DateTime from)
    {
        if (!TaskService.TryDate(task.date, out var start) || !ValidRule(task)) return null;
        var day = from.Date > start ? from.Date : start;
        var limit = TaskService.TryDate(task.repeatUntil, out var end) ? end : DateTime.MaxValue.Date;
        int interval = Math.Max(1, task.repeatInterval);
        while (day <= limit)
        {
            if (Matches(task, day)) return day;
            int distance = (day - start).Days;
            int step = 1;
            if (task.repeatUnit == RepeatUnit.Days) step = interval - distance % interval;
            if (task.repeatUnit == RepeatUnit.Weeks)
            {
                int week = (distance + ((int)start.DayOfWeek + 6) % 7) / 7;
                if (week % interval != 0) step = 7 * (interval - week % interval) - ((int)day.DayOfWeek + 6) % 7;
            }
            if (task.repeatUnit == RepeatUnit.Months || task.repeatUnit == RepeatUnit.MonthlyWeekday || task.repeatUnit == RepeatUnit.Years)
            {
                int period = task.repeatUnit == RepeatUnit.Years ? interval * 12 : interval;
                int months = (day.Year - start.Year) * 12 + day.Month - start.Month;
                int offset = months % period;
                // In the active month, search the anchor date or ordinal weekday. Otherwise skip to the next active month.
                if (offset != 0 || day.Day >= (task.repeatUnit == RepeatUnit.MonthlyWeekday ? (start.Day - 1) / 7 * 7 + 7 : start.Day)
                    || day.Day == DateTime.DaysInMonth(day.Year, day.Month))
                {
                    int jump = period - offset;
                    if (jump > (9999 - day.Year) * 12 + 12 - day.Month) return null;
                    day = new DateTime(day.Year, day.Month, 1).AddMonths(jump);
                    continue;
                }
            }
            if ((limit - day).Days < step) return null;
            day = day.AddDays(step);
        }
        return null;
    }

    public static string Label(RepeatUnit unit, int interval, DateTime start)
    {
        if (interval > 1) return $"Every {interval} {unit.ToString().ToLowerInvariant()}";
        switch (unit)
        {
            case RepeatUnit.Weeks: return "Weekly on " + start.DayOfWeek;
            case RepeatUnit.Months: return "Monthly on day " + start.Day;
            case RepeatUnit.MonthlyWeekday:
                string ordinal = new[] { "first", "second", "third", "fourth", "fifth" }[(start.Day - 1) / 7];
                return $"Monthly on the {ordinal} {start.DayOfWeek}";
            case RepeatUnit.Years: return "Yearly on " + start.ToString("MMM d", CultureInfo.InvariantCulture);
            case RepeatUnit.Weekdays: return "Every weekday";
            default: return "Daily";
        }
    }
}
