using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Tests;

public class ScheduleValidatorTests
{
    private static CalendarTask Slot(int start, int duration, bool monday = false, bool tuesday = false) =>
        new() { Start = start, Duration = duration, Monday = monday, Tuesday = tuesday };

    [Fact]
    public void Valid_schedule_has_no_issue()
    {
        var tasks = new[] { Slot(480, 210, monday: true), Slot(810, 150, monday: true, tuesday: true) };

        Assert.Empty(ScheduleValidator.Validate(tasks));
    }

    [Fact]
    public void Empty_schedule_is_valid()
    {
        Assert.Empty(ScheduleValidator.Validate([]));
    }

    [Fact]
    public void Slot_without_day_is_reported()
    {
        var issue = Assert.Single(ScheduleValidator.Validate([Slot(480, 60)]));

        Assert.Equal(new ScheduleIssue(0, ScheduleIssueKind.NoDays), issue);
    }

    [Theory]
    [InlineData(480, 0)]
    [InlineData(480, -30)]
    public void Slot_must_have_a_positive_duration(int start, int duration)
    {
        var issue = Assert.Single(ScheduleValidator.Validate([Slot(start, duration, monday: true)]));

        Assert.Equal(ScheduleIssueKind.NonPositiveDuration, issue.Kind);
    }

    [Fact]
    public void Slot_cannot_cross_midnight()
    {
        var issue = Assert.Single(ScheduleValidator.Validate([Slot(1400, 100, monday: true)]));

        Assert.Equal(ScheduleIssueKind.CrossesMidnight, issue.Kind);
    }

    [Fact]
    public void Slot_may_end_exactly_at_midnight()
    {
        Assert.Empty(ScheduleValidator.Validate([Slot(1380, 60, monday: true)]));
    }

    [Fact]
    public void Overlapping_slots_on_a_shared_day_are_reported()
    {
        var issue = Assert.Single(ScheduleValidator.Validate([Slot(480, 120, monday: true), Slot(540, 120, monday: true, tuesday: true)]));

        Assert.Equal(new ScheduleIssue(0, ScheduleIssueKind.Overlap, 1, DayOfWeek.Monday), issue);
    }

    [Fact]
    public void Overlapping_times_on_different_days_are_fine()
    {
        Assert.Empty(ScheduleValidator.Validate([Slot(480, 120, monday: true), Slot(540, 120, tuesday: true)]));
    }

    [Fact]
    public void Touching_slots_do_not_overlap()
    {
        Assert.Empty(ScheduleValidator.Validate([Slot(480, 120, monday: true), Slot(600, 60, monday: true)]));
    }
}
