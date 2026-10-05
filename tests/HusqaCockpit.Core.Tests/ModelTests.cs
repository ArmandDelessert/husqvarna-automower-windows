using System.Globalization;
using System.Text.Json;
using HusqaCockpit.Core.Json;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Tests;

public class ModelTests
{
    [Fact]
    public void Deserializes_real_world_payload()
    {
        var mower = TestData.FrontLawn();

        Assert.Equal("Front Lawn", mower.Name);
        Assert.Equal(100000001, mower.Attributes.System.SerialNumber);
        Assert.Equal(MowerActivity.ParkedInCs, mower.Activity);
        Assert.Equal(MowerState.Restricted, mower.State);
        Assert.Equal(MowerMode.MainArea, mower.Mode);
        Assert.Equal(RestrictedReason.WeekSchedule, mower.Attributes.Planner.RestrictedReason);
        Assert.Equal(OverrideAction.NotActive, mower.Attributes.Planner.Override.Action);
        Assert.Equal(HeadlightMode.EveningAndNight, mower.Attributes.Settings.Headlight.Mode);
        Assert.Equal(6, mower.Attributes.Settings.CuttingHeight);
        Assert.Equal(2, mower.Attributes.Calendar.Tasks.Count);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            mower.Attributes.Calendar.Tasks[0].Days);
        Assert.Equal(9593640, mower.Attributes.Statistics.TotalDriveDistance);
        Assert.Null(mower.Attributes.Statistics.CuttingBladeUsageTime);
        Assert.True(mower.IsConnected);
        Assert.False(mower.HasError);
        Assert.Equal(46.5, mower.LastPosition!.Latitude);
    }

    [Fact]
    public void Next_start_is_interpreted_in_the_mower_time_zone()
    {
        var mower = TestData.FrontLawn();

        // 1790236800000 is "2026-09-24 08:00" on the mower's wall clock (CEST, UTC+2).
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.FromHours(2)), mower.NextStart);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789987658000), mower.LastStatusTime);
    }

    [Fact]
    public void Error_details_are_exposed()
    {
        var mower = TestData.LoadedFleet().Find(TestData.BackGardenId)!;

        Assert.True(mower.HasError);
        Assert.False(mower.IsAlarm);
        Assert.Equal(9, mower.ErrorCode);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 11, 30, 0, TimeSpan.FromHours(2)), mower.ErrorTime);
        Assert.Null(mower.NextStart);
        Assert.Null(mower.LastPosition);
    }

    [Fact]
    public void Unknown_enum_values_do_not_break_deserialization()
    {
        var status = JsonSerializer.Deserialize<MowerStatus>(
            """{ "activity": "TELEPORTING", "state": "IN_OPERATION", "mode": "SOMETHING_NEW" }""",
            AutomowerJson.Options)!;

        Assert.Equal(MowerActivity.Unknown, status.Activity);
        Assert.Equal(MowerState.InOperation, status.State);
        Assert.Equal(MowerMode.Unknown, status.Mode);
    }

    [Fact]
    public void Enums_serialize_to_upper_snake_case()
    {
        Assert.Equal("\"EVENING_AND_NIGHT\"", JsonSerializer.Serialize(HeadlightMode.EveningAndNight, AutomowerJson.Options));
    }

    [Fact]
    public void Work_area_id_accepts_string_or_number()
    {
        var fromString = JsonSerializer.Deserialize<MowerStatus>("""{ "workAreaId": "78555" }""", AutomowerJson.Options)!;
        var fromNumber = JsonSerializer.Deserialize<MowerStatus>("""{ "workAreaId": 78555 }""", AutomowerJson.Options)!;

        Assert.Equal(78555, fromString.WorkAreaId);
        Assert.Equal(78555, fromNumber.WorkAreaId);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1728034996, "2024-10-04T09:43:16+02:00")] // seconds
    [InlineData(1728034996000, "2024-10-04T09:43:16+02:00")] // milliseconds
    [InlineData(1704099600000, "2024-01-01T09:00:00+01:00")] // winter time
    public void Mower_local_timestamps(long timestamp, string? expected)
    {
        var result = MowerTime.FromMowerLocal(timestamp, TestData.Zurich);

        Assert.Equal(expected is null ? null : DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public void Timestamp_inside_dst_gap_is_moved_after_the_gap()
    {
        // 2026-03-29 02:30 does not exist in Zurich (clocks jump from 02:00 to 03:00).
        var wallClock = new DateTime(2026, 3, 29, 2, 30, 0);
        var timestamp = (long)(wallClock - DateTime.UnixEpoch).TotalMilliseconds;

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)), MowerTime.FromMowerLocal(timestamp, TestData.Zurich));
    }
}
