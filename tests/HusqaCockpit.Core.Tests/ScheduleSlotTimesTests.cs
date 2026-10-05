using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Tests;

public class ScheduleSlotTimesTests
{
    private static TimeSpan At(int hours, int minutes = 0) => new(hours, minutes, 0);

    /// <summary>The task the schedule editor sends for a row with these clock times, on Mondays.</summary>
    private static CalendarTask Row(TimeSpan start, TimeSpan end)
    {
        var (startMinutes, duration) = ScheduleSlotTimes.FromClockTimes(start, end);
        return new CalendarTask { Start = startMinutes, Duration = duration, Monday = true };
    }

    private static void AssertConvertsBothWays(int start, int duration, TimeSpan startTime, TimeSpan endTime)
    {
        Assert.Equal((startTime, endTime), ScheduleSlotTimes.ToClockTimes(start, duration));
        Assert.Equal((start, duration), ScheduleSlotTimes.FromClockTimes(startTime, endTime));
    }

    [Fact]
    public void Whole_day_is_shown_from_00_00_to_00_00()
    {
        AssertConvertsBothWays(0, 1440, At(0), At(0));
        Assert.Empty(ScheduleValidator.Validate([Row(At(0), At(0))]));
    }

    [Fact]
    public void Slot_ending_at_midnight_is_shown_with_an_end_of_00_00()
    {
        AssertConvertsBothWays(1320, 120, At(22), At(0));
        Assert.Empty(ScheduleValidator.Validate([Row(At(22), At(0))]));
    }

    [Fact]
    public void Ordinary_slot_keeps_its_times()
    {
        AssertConvertsBothWays(480, 210, At(8), At(11, 30));
        Assert.Empty(ScheduleValidator.Validate([Row(At(8), At(11, 30))]));
    }

    [Theory]
    [InlineData(10, 8)]
    [InlineData(10, 10)]
    [InlineData(22, 1)]
    public void Slot_ending_at_or_before_its_start_is_still_rejected(int startHour, int endHour)
    {
        var issue = Assert.Single(ScheduleValidator.Validate([Row(At(startHour), At(endHour))]));

        Assert.Equal(ScheduleIssueKind.NonPositiveDuration, issue.Kind);
    }

    [Fact]
    public void Every_valid_slot_fits_on_the_clock_and_converts_back_unchanged()
    {
        // Every 5 minutes, slots ending at midnight included.
        var slots = new List<(int Start, int Duration)>();
        for (var start = 0; start < ScheduleValidator.MinutesPerDay; start += 5)
        {
            for (var duration = 5; start + duration <= ScheduleValidator.MinutesPerDay; duration += 5)
            {
                slots.Add((start, duration));
            }
        }

        Assert.All(slots, slot =>
        {
            var (startTime, endTime) = ScheduleSlotTimes.ToClockTimes(slot.Start, slot.Duration);
            Assert.InRange(startTime, TimeSpan.Zero, At(23, 59));
            Assert.InRange(endTime, TimeSpan.Zero, At(23, 59));
            Assert.Equal(slot, ScheduleSlotTimes.FromClockTimes(startTime, endTime));
        });
    }
}
