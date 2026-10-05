namespace HusqaCockpit.Core.Models;

/// <summary>
/// Converts a schedule slot between its API form (start and duration in minutes) and the start and end
/// clock times shown in the schedule editor. A clock stops at 23:59, so a slot that ends at midnight (24:00)
/// shows 00:00 as its end, and an end of 00:00 is read back as midnight at the end of the day.
/// </summary>
public static class ScheduleSlotTimes
{
    /// <summary>The clock times of a slot: 480 + 210 gives 08:00–11:30, 1320 + 120 gives 22:00–00:00.</summary>
    public static (TimeSpan Start, TimeSpan End) ToClockTimes(int start, int duration) =>
        (TimeSpan.FromMinutes(start), TimeSpan.FromMinutes((start + duration) % ScheduleValidator.MinutesPerDay));

    /// <summary>
    /// The start and duration of the slot between two clock times. An end of 00:00 is midnight at the end of the day:
    /// 22:00–00:00 lasts 2 hours and 00:00–00:00 is the whole day. Any other end at or before the start gives a
    /// duration that <see cref="ScheduleValidator"/> rejects.
    /// </summary>
    public static (int Start, int Duration) FromClockTimes(TimeSpan start, TimeSpan end)
    {
        var startMinutes = (int)start.TotalMinutes;
        var endMinutes = (int)end.TotalMinutes;
        if (endMinutes == 0)
        {
            endMinutes = ScheduleValidator.MinutesPerDay;
        }
        return (startMinutes, endMinutes - startMinutes);
    }
}
