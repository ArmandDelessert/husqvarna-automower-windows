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

        Assert.Empty(AlertDetector.Detect(s_healthy, mowing));
    }
}
