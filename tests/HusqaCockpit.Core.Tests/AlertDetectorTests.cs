using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Tests;

public class AlertDetectorTests
{
    private static readonly Mower s_healthy = TestData.FrontLawn();

    [Fact]
    public void Healthy_mower_seen_for_the_first_time_raises_nothing()
    {
        Assert.Empty(AlertDetector.Detect(null, s_healthy));
    }

    [Fact]
    public void Disconnected_mower_at_startup_raises_nothing()
    {
        Assert.Empty(AlertDetector.Detect(null, s_healthy.With(connected: false)));
    }

    [Fact]
    public void Existing_error_at_startup_is_reported()
    {
        var alert = Assert.Single(AlertDetector.Detect(null, s_healthy.With(state: MowerState.Error, errorCode: 9)));

        Assert.Equal(MowerAlertKind.Error, alert.Kind);
        Assert.Equal(9, alert.ErrorCode);
    }

    [Fact]
    public void New_error_is_reported()
    {
        var alert = Assert.Single(AlertDetector.Detect(s_healthy, s_healthy.With(state: MowerState.Error, errorCode: 14)));

        Assert.Equal(MowerAlertKind.Error, alert.Kind);
        Assert.Equal(14, alert.ErrorCode);
    }

    [Fact]
    public void Same_error_is_not_reported_twice()
    {
        var inError = s_healthy.With(state: MowerState.Error, errorCode: 14);

        Assert.Empty(AlertDetector.Detect(inError, inError with { }));
    }

    [Fact]
    public void Different_error_is_reported()
    {
        var first = s_healthy.With(state: MowerState.Error, errorCode: 14);
        var second = s_healthy.With(state: MowerState.Error, errorCode: 9);

        Assert.Equal(9, Assert.Single(AlertDetector.Detect(first, second)).ErrorCode);
    }

    [Theory]
    [InlineData(69)]
    [InlineData(74)]
    public void Theft_alarm_codes_are_alarms(int code)
    {
        var alert = Assert.Single(AlertDetector.Detect(s_healthy, s_healthy.With(state: MowerState.Error, errorCode: code)));

        Assert.Equal(MowerAlertKind.Alarm, alert.Kind);
    }

    [Fact]
    public void Recovery_is_reported_with_the_previous_code()
    {
        var alert = Assert.Single(AlertDetector.Detect(s_healthy.With(state: MowerState.Error, errorCode: 14), s_healthy));

        Assert.Equal(MowerAlertKind.ErrorCleared, alert.Kind);
        Assert.Equal(14, alert.ErrorCode);
    }

    [Fact]
    public void Stopped_mower_is_reported()
    {
        var alert = Assert.Single(AlertDetector.Detect(s_healthy, s_healthy.With(state: MowerState.Stopped)));

        Assert.Equal(MowerAlertKind.Stopped, alert.Kind);
    }

    [Fact]
    public void Connectivity_changes_are_reported()
    {
        var offline = s_healthy.With(connected: false);

        Assert.Equal(MowerAlertKind.Disconnected, Assert.Single(AlertDetector.Detect(s_healthy, offline)).Kind);
        Assert.Equal(MowerAlertKind.Reconnected, Assert.Single(AlertDetector.Detect(offline, s_healthy)).Kind);
    }

    [Fact]
    public void Ordinary_activity_changes_raise_nothing()
    {
        var mowing = s_healthy.With(state: MowerState.InOperation, activity: MowerActivity.Mowing);
        var goingHome = mowing.With(activity: MowerActivity.GoingHome);

        Assert.Empty(AlertDetector.Detect(mowing, goingHome));
    }

    // ----- Start and end of a mowing task -----

    private static readonly Mower s_parked = s_healthy.With(
        state: MowerState.Restricted, activity: MowerActivity.ParkedInCs, restrictedReason: RestrictedReason.WeekSchedule);

    private static Mower Working(MowerActivity activity, int battery = 80) =>
        s_healthy.With(state: MowerState.InOperation, activity: activity, battery: battery, restrictedReason: RestrictedReason.None);

    [Fact]
    public void Leaving_the_station_when_the_slot_opens_starts_a_task()
    {
        Assert.Equal([MowerAlertKind.TaskStarted], TestData.AlertKinds(s_parked, Working(MowerActivity.Leaving, 100), Working(MowerActivity.Mowing, 99)));
    }

    [Fact]
    public void A_mower_that_charges_before_leaving_starts_its_task_once()
    {
        // Low battery when the slot opens: the state is already IN_OPERATION while it charges ("CHARGING due to low battery").
        Assert.Equal(
            [MowerAlertKind.TaskStarted],
            TestData.AlertKinds(s_parked, Working(MowerActivity.Charging, 30), Working(MowerActivity.Leaving, 100), Working(MowerActivity.Mowing, 99)));
    }

    [Fact]
    public void Going_back_to_charge_in_the_middle_of_a_task_and_leaving_again_raises_nothing()
    {
        Assert.Empty(TestData.AlertKinds(
            Working(MowerActivity.Mowing, 40),
            Working(MowerActivity.GoingHome, 12),
            Working(MowerActivity.Charging, 12),
            Working(MowerActivity.Charging, 70),
            Working(MowerActivity.Leaving, 100),
            Working(MowerActivity.Mowing, 98)));
    }

    [Fact]
    public void A_full_battery_alone_is_not_the_end_of_the_task()
    {
        // Still in operation at 100 %: it is about to leave the station again.
        Assert.Empty(TestData.AlertKinds(Working(MowerActivity.Charging, 95), Working(MowerActivity.Charging, 100), Working(MowerActivity.Leaving, 100)));
    }

    [Theory]
    [InlineData(MowerActivity.GoingHome, 55, RestrictedReason.WeekSchedule)]
    [InlineData(MowerActivity.ParkedInCs, 100, RestrictedReason.WeekSchedule)]
    [InlineData(MowerActivity.ParkedInCs, 20, RestrictedReason.ParkOverride)]
    [InlineData(MowerActivity.GoingHome, 70, RestrictedReason.DailyLimit)]
    public void A_task_ends_when_the_mower_can_no_longer_mow(MowerActivity activity, int battery, RestrictedReason reason)
    {
        var ended = s_healthy.With(state: MowerState.Restricted, activity: activity, battery: battery, restrictedReason: reason);

        var alert = Assert.Single(AlertDetector.Detect(Working(MowerActivity.Mowing), ended));

        Assert.Equal(MowerAlertKind.TaskFinished, alert.Kind);
    }

    [Fact]
    public void A_full_working_day_raises_one_start_and_one_end()
    {
        Assert.Equal(
            [MowerAlertKind.TaskStarted, MowerAlertKind.TaskFinished],
            TestData.AlertKinds(
                s_parked,
                Working(MowerActivity.Leaving, 100),
                Working(MowerActivity.Mowing, 100),
                Working(MowerActivity.GoingHome, 15),
                Working(MowerActivity.Charging, 15),
                Working(MowerActivity.Leaving, 100),
                Working(MowerActivity.Mowing, 100),
                Working(MowerActivity.Mowing, 60),
                s_parked.With(activity: MowerActivity.GoingHome, battery: 55),
                s_parked.With(activity: MowerActivity.ParkedInCs, battery: 55),
                s_parked.With(activity: MowerActivity.Charging, battery: 70)));
    }

    [Fact]
    public void Pausing_and_resuming_neither_ends_nor_starts_a_task()
    {
        Assert.Empty(TestData.AlertKinds(
            Working(MowerActivity.Mowing),
            Working(MowerActivity.Mowing).With(state: MowerState.Paused, activity: MowerActivity.StoppedInGarden),
            Working(MowerActivity.Mowing)));
    }

    [Fact]
    public void Stopping_and_resuming_only_reports_the_stop()
    {
        Assert.Equal(
            [MowerAlertKind.Stopped],
            TestData.AlertKinds(
                Working(MowerActivity.Mowing),
                Working(MowerActivity.Mowing).With(state: MowerState.Stopped, activity: MowerActivity.StoppedInGarden),
                Working(MowerActivity.Mowing)));
    }

    [Fact]
    public void An_error_in_the_middle_of_a_task_is_not_an_end_and_its_recovery_not_a_start()
    {
        var failed = Working(MowerActivity.StoppedInGarden).With(state: MowerState.Error, errorCode: 9);

        Assert.Equal(
            [MowerAlertKind.Error, MowerAlertKind.ErrorCleared],
            TestData.AlertKinds(Working(MowerActivity.Mowing), failed, Working(MowerActivity.Mowing)));
    }

    [Fact]
    public void Becoming_restricted_with_an_error_is_reported_as_the_error_only()
    {
        var kinds = TestData.AlertKinds(Working(MowerActivity.Mowing), s_parked.With(state: MowerState.Restricted, errorCode: 9));

        Assert.Equal([MowerAlertKind.Error], kinds);
    }

    [Fact]
    public void Opening_the_app_on_a_mower_that_is_mowing_raises_nothing()
    {
        Assert.Empty(AlertDetector.Detect(null, Working(MowerActivity.Mowing)));
    }

    [Fact]
    public void Repeating_the_same_state_raises_nothing()
    {
        Assert.Empty(TestData.AlertKinds(Working(MowerActivity.Mowing), Working(MowerActivity.Mowing), Working(MowerActivity.Mowing)));
        Assert.Empty(TestData.AlertKinds(s_parked, s_parked, s_parked));
    }

    [Fact]
    public void Losing_and_regaining_the_connection_in_the_middle_of_a_task_raises_only_the_connection_alerts()
    {
        Assert.Equal(
            [MowerAlertKind.Disconnected, MowerAlertKind.Reconnected],
            TestData.AlertKinds(
                Working(MowerActivity.Mowing),
                Working(MowerActivity.Mowing).With(connected: false),
                Working(MowerActivity.Mowing)));
    }

    [Fact]
    public void A_task_that_ended_while_the_mower_was_out_of_reach_is_not_reported_late()
    {
        Assert.Equal(
            [MowerAlertKind.Disconnected, MowerAlertKind.Reconnected],
            TestData.AlertKinds(Working(MowerActivity.Mowing), Working(MowerActivity.Mowing).With(connected: false), s_parked));
    }

    [Fact]
    public void A_task_that_started_while_the_mower_was_out_of_reach_is_not_reported_late()
    {
        Assert.Equal(
            [MowerAlertKind.Disconnected, MowerAlertKind.Reconnected],
            TestData.AlertKinds(s_parked, s_parked.With(connected: false), Working(MowerActivity.Mowing)));
    }

    [Fact]
    public void A_mower_that_loses_its_connection_while_changing_state_reports_only_the_connection()
    {
        Assert.Equal(
            [MowerAlertKind.Disconnected],
            TestData.AlertKinds(Working(MowerActivity.Mowing), s_parked.With(connected: false)));
    }
}
