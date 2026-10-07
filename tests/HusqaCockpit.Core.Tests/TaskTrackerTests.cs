using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;
using Microsoft.Extensions.Time.Testing;

namespace HusqaCockpit.Core.Tests;

public class TaskTrackerTests
{
    // Monday 5 October 2026, 08:00 in Zurich (CEST, UTC+2): the start of the first slot of the fixture mower.
    private static readonly DateTimeOffset s_slotStart = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);

    private readonly Mower _working = TestData.FrontLawn().With(state: MowerState.InOperation, activity: MowerActivity.Mowing);
    private FakeTimeProvider _time = new(s_slotStart);
    private TaskTracker _tracker;

    public TaskTrackerTests() => _tracker = new TaskTracker(_time);

    /// <summary>Starts again from another time (a fake clock cannot go back).</summary>
    private void SetClock(DateTimeOffset utc)
    {
        _time = new FakeTimeProvider(utc);
        _tracker = new TaskTracker(_time);
    }

    private MowerAlert Started(Mower? mower = null) => _tracker.Enrich(new MowerAlert(MowerAlertKind.TaskStarted, mower ?? _working));

    private MowerAlert Finished(Mower? mower = null) => _tracker.Enrich(new MowerAlert(MowerAlertKind.TaskFinished, mower ?? _working));

    // ----- Why a task started -----

    [Fact]
    public void A_start_inside_a_scheduled_slot_is_the_schedule()
    {
        Assert.Equal(TaskStartReason.Schedule, Started().StartReason);
    }

    [Fact]
    public void A_start_a_little_before_the_slot_is_still_the_schedule()
    {
        SetClock(s_slotStart - TimeSpan.FromMinutes(4));

        Assert.Equal(TaskStartReason.Schedule, Started().StartReason);
    }

    [Fact]
    public void A_start_well_before_the_slot_is_not_explained()
    {
        SetClock(s_slotStart - TimeSpan.FromMinutes(30));

        Assert.Equal(TaskStartReason.Unknown, Started().StartReason);
    }

    [Fact]
    public void A_start_after_the_end_of_the_slot_is_not_explained()
    {
        // The first slot is 08:00–11:30 and the second 13:30–16:00 (Monday): 12:00 is between them.
        SetClock(s_slotStart + TimeSpan.FromHours(4));

        Assert.Equal(TaskStartReason.Unknown, Started().StartReason);
    }

    [Fact]
    public void A_start_on_a_day_without_slot_is_not_explained()
    {
        // Wednesday 08:00: the fixture has no slot on Wednesdays.
        SetClock(s_slotStart + TimeSpan.FromDays(2));

        Assert.Equal(TaskStartReason.Unknown, Started().StartReason);
    }

    [Fact]
    public void A_start_without_any_schedule_is_not_explained()
    {
        Assert.Equal(TaskStartReason.Unknown, Started(_working.With(schedule: [])).StartReason);
    }

    [Fact]
    public void The_slots_are_read_in_the_time_zone_of_the_mower()
    {
        // 08:00 UTC is 10:00 in Zurich: inside the first slot. 06:00 UTC is 08:00 in Zurich (the other tests).
        SetClock(s_slotStart + TimeSpan.FromHours(2));

        Assert.Equal(TaskStartReason.Schedule, Started().StartReason);
    }

    [Fact]
    public void A_forced_mowing_is_a_manual_start_even_inside_a_slot()
    {
        Assert.Equal(TaskStartReason.Manual, Started(_working.With(overrideAction: OverrideAction.ForceMow)).StartReason);
    }

    [Fact]
    public void A_start_command_sent_by_the_app_a_moment_ago_is_the_applications()
    {
        _tracker.NoteStartCommand(_working.Id);
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(TaskStartReason.Application, Started(_working.With(overrideAction: OverrideAction.ForceMow)).StartReason);
    }

    [Fact]
    public void A_start_command_is_forgotten_after_two_minutes()
    {
        _tracker.NoteStartCommand(_working.Id);
        _time.Advance(TaskTracker.CommandWindow + TimeSpan.FromSeconds(1));

        Assert.Equal(TaskStartReason.Schedule, Started().StartReason);
    }

    [Fact]
    public void A_start_command_for_another_mower_does_not_count()
    {
        _tracker.NoteStartCommand("another-mower");

        Assert.Equal(TaskStartReason.Schedule, Started().StartReason);
    }

    [Fact]
    public void Only_commands_that_can_make_a_mower_leave_are_start_commands()
    {
        Assert.True(MowerAction.Start(TimeSpan.FromHours(1)).CanStartMowing);
        Assert.True(MowerAction.ResumeSchedule().CanStartMowing);
        Assert.False(MowerAction.Pause().CanStartMowing);
        Assert.False(MowerAction.Park(TimeSpan.FromHours(1)).CanStartMowing);
        Assert.False(MowerAction.ParkUntilNextSchedule().CanStartMowing);
        Assert.False(MowerAction.ParkUntilFurtherNotice().CanStartMowing);
    }

    // ----- How long a task lasted -----

    [Fact]
    public void A_finished_task_reports_how_long_it_lasted()
    {
        Started();
        _time.Advance(TimeSpan.FromMinutes(125));

        Assert.Equal(TimeSpan.FromMinutes(125), Finished().Duration);
    }

    [Fact]
    public void A_task_whose_start_was_not_seen_has_no_duration()
    {
        Assert.Null(Finished().Duration);
    }

    [Fact]
    public void A_start_is_used_for_one_end_only()
    {
        Started();
        _time.Advance(TimeSpan.FromMinutes(30));
        Finished();
        _time.Advance(TimeSpan.FromMinutes(30));

        Assert.Null(Finished().Duration);
    }

    [Fact]
    public void The_latest_start_counts()
    {
        Started();
        _time.Advance(TimeSpan.FromHours(5));
        Started();
        _time.Advance(TimeSpan.FromMinutes(40));

        Assert.Equal(TimeSpan.FromMinutes(40), Finished().Duration);
    }

    [Fact]
    public void Each_mower_is_timed_on_its_own()
    {
        var other = _working with { Id = "another-mower" };
        Started();
        _time.Advance(TimeSpan.FromMinutes(10));
        Started(other);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), Finished(other).Duration);
        Assert.Equal(TimeSpan.FromMinutes(15), Finished().Duration);
    }

    [Fact]
    public void Losing_the_connection_forgets_the_start()
    {
        Started();
        _time.Advance(TimeSpan.FromMinutes(20));
        _tracker.Enrich(new MowerAlert(MowerAlertKind.Disconnected, _working));
        _time.Advance(TimeSpan.FromMinutes(20));

        Assert.Null(Finished().Duration);
    }

    [Fact]
    public void A_start_older_than_a_day_is_stale_and_not_reported()
    {
        Started();
        _time.Advance(TaskTracker.MaximumTaskDuration + TimeSpan.FromMinutes(1));

        Assert.Null(Finished().Duration);
    }

    [Fact]
    public void Other_alerts_pass_through_unchanged()
    {
        var alert = new MowerAlert(MowerAlertKind.Error, _working, ErrorCode: 9);

        Assert.Same(alert, _tracker.Enrich(alert));
    }
}
