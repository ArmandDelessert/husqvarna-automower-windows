namespace HusqaCockpit.Core.Models;

public enum ScheduleIssueKind
{
    /// <summary>The slot is not active on any day.</summary>
    NoDays,

    /// <summary>The slot ends at or before its start.</summary>
    NonPositiveDuration,

    /// <summary>The slot runs past midnight (not supported by the mower).</summary>
    CrossesMidnight,

    /// <summary>Two slots overlap on the same day.</summary>
    Overlap,
}

/// <param name="Index">Zero-based index of the faulty slot.</param>
/// <param name="OtherIndex">For overlaps: the other slot.</param>
/// <param name="Day">For overlaps: the day on which they overlap.</param>
public sealed record ScheduleIssue(int Index, ScheduleIssueKind Kind, int? OtherIndex = null, DayOfWeek? Day = null);

/// <summary>Checks a weekly schedule before it is sent to the mower.</summary>
public static class ScheduleValidator
{
    public const int MinutesPerDay = 24 * 60;

    public static IReadOnlyList<ScheduleIssue> Validate(IReadOnlyList<CalendarTask> tasks)
    {
        var issues = new List<ScheduleIssue>();
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (!task.Days.Any())
            {
                issues.Add(new ScheduleIssue(i, ScheduleIssueKind.NoDays));
            }
            if (task.Duration <= 0)
            {
                issues.Add(new ScheduleIssue(i, ScheduleIssueKind.NonPositiveDuration));
            }
            else if (task.Start < 0 || task.Start + task.Duration > MinutesPerDay)
            {
                issues.Add(new ScheduleIssue(i, ScheduleIssueKind.CrossesMidnight));
            }
        }

        for (var i = 0; i < tasks.Count; i++)
        {
            for (var j = i + 1; j < tasks.Count; j++)
            {
                if (tasks[i].Duration <= 0 || tasks[j].Duration <= 0)
                {
                    continue;
                }

                // Slots that merely touch (one ends when the next starts) do not overlap.
                var overlapInTime = tasks[i].Start < tasks[j].Start + tasks[j].Duration
                    && tasks[j].Start < tasks[i].Start + tasks[i].Duration;
                if (!overlapInTime)
                {
                    continue;
                }

                var sharedDay = tasks[i].Days.Intersect(tasks[j].Days).Cast<DayOfWeek?>().FirstOrDefault();
                if (sharedDay is not null)
                {
                    issues.Add(new ScheduleIssue(i, ScheduleIssueKind.Overlap, j, sharedDay));
                }
            }
        }
        return issues;
    }
}
