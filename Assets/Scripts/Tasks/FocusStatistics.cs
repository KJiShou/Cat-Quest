using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public enum FocusStatisticsPeriod { Day, Week, Month, Year }

// Read-only projection of committed receipts. Never uses reward-capped minutes
// when actual elapsed time is available, and never changes the player's save.
public static class FocusStatistics
{
    public const string Untagged = "__untagged";
    public sealed class Series
    {
        public string id, name, color;
        public double seconds;
        public double[] buckets;
    }
    public sealed class Result
    {
        public DateTime start, end;
        public string[] labels;
        public double[] buckets;
        public double seconds;
        public int sessions;
        public bool legacyMultipleTags;
        public List<Series> tags = new List<Series>();
    }
    public static DateTime Start(DateTime date, FocusStatisticsPeriod period)
    {
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        switch (period)
        {
            case FocusStatisticsPeriod.Week: return date.AddDays(-((int)date.DayOfWeek + 6) % 7);
            case FocusStatisticsPeriod.Month: return new DateTime(date.Year, date.Month, 1);
            case FocusStatisticsPeriod.Year: return new DateTime(date.Year, 1, 1);
            default: return date;
        }
    }
    public static DateTime Move(DateTime date, FocusStatisticsPeriod period, int direction)
    {
        date = Start(date, period);
        switch (period)
        {
            case FocusStatisticsPeriod.Week: return date.AddDays(7 * direction);
            case FocusStatisticsPeriod.Month: return date.AddMonths(direction);
            case FocusStatisticsPeriod.Year: return date.AddYears(direction);
            default: return date.AddDays(direction);
        }
    }
    public static string Duration(double seconds)
    {
        if (seconds > 0 && seconds < 60) return "<1 min";
        long minutes = (long)(Math.Max(0, seconds) / 60);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes} min";
    }
    public static Result Query(IEnumerable<FocusSession> sessions, IEnumerable<QuestTag> catalog,
        DateTime date, FocusStatisticsPeriod period, TimeZoneInfo zone)
    {
        var result = new Result { start = Start(date, period) };
        result.end = Move(result.start, period, 1);
        int count = period == FocusStatisticsPeriod.Day ? 24 : period == FocusStatisticsPeriod.Year ? 12 : (result.end - result.start).Days;
        result.buckets = new double[count]; result.labels = new string[count];
        var boundaries = new DateTime[count + 1];
        for (int i = 0; i <= count; i++)
        {
            var local = period == FocusStatisticsPeriod.Day ? result.start.AddHours(i)
                : period == FocusStatisticsPeriod.Year ? result.start.AddMonths(i) : result.start.AddDays(i);
            // A skipped local hour has zero width. Repeated hours remain in one
            // local-hour bucket, so a DST day still conserves actual elapsed time.
            var valid = local;
            while (zone.IsInvalidTime(valid)) valid = valid.AddMinutes(1);
            boundaries[i] = zone.IsAmbiguousTime(valid)
                ? DateTime.SpecifyKind(valid - zone.GetAmbiguousTimeOffsets(valid).Max(), DateTimeKind.Utc)
                : TimeZoneInfo.ConvertTimeToUtc(valid, zone);
            if (i < count) result.labels[i] = local.ToString(period == FocusStatisticsPeriod.Day ? "HH": period == FocusStatisticsPeriod.Week ? "ddd" : period == FocusStatisticsPeriod.Year ? "MMM" : "dd", CultureInfo.InvariantCulture);
        }
        var known = (catalog ?? Enumerable.Empty<QuestTag>()).Where(t => t != null).GroupBy(t => t.id ?? "").ToDictionary(g => g.Key, g => g.First());
        var series = new Dictionary<string, Series>();
        var seen = new HashSet<string>();
        foreach (var session in sessions ?? Enumerable.Empty<FocusSession>())
        {
            if (session == null || (session.status != FocusSessionStatus.Completed && session.status != FocusSessionStatus.EndedEarly)) continue;
            if (!string.IsNullOrEmpty(session.id) && !seen.Add(session.id)) continue;
            double seconds = session.elapsedSeconds > 0 ? session.elapsedSeconds : session.eligibleSeconds > 0 ? session.eligibleSeconds : session.eligibleMinutes * 60d;
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) continue;
            if (!DateTime.TryParse(session.completedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var end)) continue;
            if (seconds > (end - DateTime.MinValue).TotalSeconds) continue;
            var begin = end.AddSeconds(-seconds);
            double overlap = Math.Max(0, (Min(end, boundaries[count]) - Max(begin, boundaries[0])).TotalSeconds);
            if (overlap <= 0) continue;
            result.seconds += overlap; result.sessions++;
            var ids = (session.tagIds ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            result.legacyMultipleTags |= ids.Count > 1;
            if (ids.Count == 0) ids.Add(Untagged);
            foreach (var id in ids)
            {
                if (!series.TryGetValue(id, out var tag))
                {
                    known.TryGetValue(id, out var current);
                    int index = session.tagIds?.IndexOf(id) ?? -1;
                    string oldName = index >= 0 && index < (session.tagNames?.Count ?? 0) ? session.tagNames[index] : null;
                    tag = new Series { id = id, name = id == Untagged ? "No tag" : current?.name ?? oldName ?? "Deleted tag", color = current?.colorHex ?? "#5CA6CE", buckets = new double[count] };
                    series.Add(id, tag);
                }
                tag.seconds += overlap;
            }
            for (int i = 0; i < count; i++)
            {
                double part = Math.Max(0, (Min(end, boundaries[i+1]) - Max(begin, boundaries[i])).TotalSeconds);
                result.buckets[i] += part;
                foreach (var id in ids) series[id].buckets[i] += part;
            }
        }
        result.tags = series.Values.OrderByDescending(t => t.seconds).ThenBy(t => t.name, StringComparer.Ordinal).ToList();
        return result;
    }
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
