using HusqaCockpit.Core.Models;
using HusqaCockpit.Presentation.Tests.Support;

namespace HusqaCockpit.Presentation.Tests;

public sealed class MowerFormatterTests : IDisposable
{
    // Wednesday 10 June 2026, 12:00 local time.
    private static readonly DateTimeOffset s_now = TestMowers.Now;

    private readonly IDisposable _culture = TestCulture.Use(TestCulture.English);
    private readonly MowerFormatter _formatter = new(ReswStrings.English);

    public void Dispose() => _culture.Dispose();

    // ----- Summarize -----

    [Fact]
    public void Disconnected_mower_tells_when_it_was_last_heard_of()
    {
        var summary = Summarize(TestMowers.Parked() with
        {
            Metadata = new MetadataInfo { Connected = false, StatusTimestamp = s_now.AddHours(-3).ToUnixTimeMilliseconds() },
        });

        Assert.Equal(new StatusSummary("Disconnected", "Last contact 3 h ago", StatusSeverity.Offline), summary);
    }

    [Fact]
    public void Disconnected_mower_never_heard_of_has_no_detail()
    {
        var summary = Summarize(TestMowers.Parked() with { Metadata = new MetadataInfo { Connected = false } });

        Assert.Equal(new StatusSummary("Disconnected", "", StatusSeverity.Offline), summary);
    }

    [Fact]
    public void Error_shows_the_error_text_and_since_when()
    {
        var attributes = TestMowers.InError(1);
        var summary = Summarize(attributes with { Mower = attributes.Mower with { ErrorCodeTimestamp = MowerLocal(10, 10, 30) } });

        Assert.Equal(new StatusSummary("Outside working area", "Since today at 10:30", StatusSeverity.Error), summary);
    }

    [Fact]
    public void Error_state_without_a_code_shows_the_state()
    {
        var summary = Summarize(TestMowers.Parked() with { Mower = new MowerStatus { State = MowerState.ErrorAtPowerUp } });

        Assert.Equal(new StatusSummary("Error at start-up", "", StatusSeverity.Error), summary);
    }

    [Fact]
    public void Unknown_error_code_still_gives_a_title()
    {
        var summary = Summarize(TestMowers.InError(9999));

        Assert.Equal("Unknown error (code 9999)", summary.Title);
    }

    [Theory]
    [InlineData(MowerState.Off, "Switched off")]
    [InlineData(MowerState.WaitUpdating, "Software update in progress")]
    [InlineData(MowerState.WaitPowerUp, "Starting up")]
    public void Unavailable_states_are_information(MowerState state, string title)
    {
        var summary = Summarize(TestMowers.Parked() with { Mower = new MowerStatus { State = state } });

        Assert.Equal(new StatusSummary(title, "", StatusSeverity.Info), summary);
    }

    [Fact]
    public void Stopped_mower_needs_a_manual_action()
    {
        var summary = Summarize(TestMowers.Parked() with { Mower = new MowerStatus { State = MowerState.Stopped } });

        Assert.Equal(new StatusSummary("Stopped", "Manual action required on the mower", StatusSeverity.Warning), summary);
    }

    [Fact]
    public void Paused_mower_waits_in_the_garden()
    {
        var summary = Summarize(TestMowers.Parked() with { Mower = new MowerStatus { State = MowerState.Paused } });

        Assert.Equal(new StatusSummary("Paused", "Waiting in the garden", StatusSeverity.Warning), summary);
    }

    [Fact]
    public void Mowing_on_schedule()
    {
        Assert.Equal(new StatusSummary("Mowing", "Following the schedule", StatusSeverity.Ok), Summarize(TestMowers.Mowing()));
    }

    [Fact]
    public void Mowing_after_a_manual_start()
    {
        var summary = Summarize(TestMowers.Mowing() with
        {
            Planner = new PlannerInfo { Override = new PlannerOverride { Action = OverrideAction.ForceMow } },
        });

        Assert.Equal(new StatusSummary("Mowing", "Manual start (outside the schedule)", StatusSeverity.Ok), summary);
    }

    [Fact]
    public void Mowing_while_searching_for_satellites()
    {
        var attributes = TestMowers.Mowing();
        var summary = Summarize(attributes with { Mower = attributes.Mower with { InactiveReason = InactiveReason.SearchingForSatellites } });

        Assert.Equal(new StatusSummary("Mowing", "Searching for satellites", StatusSeverity.Ok), summary);
    }

    [Fact]
    public void Charging_tells_the_remaining_time()
    {
        var summary = Summarize(Charging(TestMowers.Parked()) with { Battery = new BatteryInfo { BatteryPercent = 40, RemainingChargingTime = 45 * 60 } });

        Assert.Equal(new StatusSummary("Charging", "45 min left", StatusSeverity.Ok), summary);
    }

    [Fact]
    public void Charging_without_a_remaining_time_tells_the_next_start()
    {
        var summary = Summarize(Charging(WithNextStart(TestMowers.Parked(), MowerLocal(11, 8))));

        Assert.Equal(new StatusSummary("Charging", "Next start tomorrow at 08:00", StatusSeverity.Ok), summary);
    }

    [Fact]
    public void Parked_on_schedule_tells_the_next_start()
    {
        var summary = Summarize(WithNextStart(TestMowers.Parked(), MowerLocal(11, 8)));

        Assert.Equal(new StatusSummary("Parked", "Next start tomorrow at 08:00", StatusSeverity.Ok), summary);
    }

    [Fact]
    public void Parked_without_a_schedule()
    {
        Assert.Equal(new StatusSummary("Parked", "No upcoming start", StatusSeverity.Ok), Summarize(TestMowers.Parked()));
    }

    [Fact]
    public void Parked_until_further_notice()
    {
        var summary = Summarize(TestMowers.Parked() with { Planner = new PlannerInfo { RestrictedReason = RestrictedReason.ParkOverride } });

        Assert.Equal(
            new StatusSummary("Parked until further notice", "Use “Resume” to follow the schedule again", StatusSeverity.Info),
            summary);
    }

    [Fact]
    public void Parked_for_a_while_tells_until_when()
    {
        var summary = Summarize(TestMowers.Parked() with
        {
            Planner = new PlannerInfo { RestrictedReason = RestrictedReason.ParkOverride, NextStartTimestamp = MowerLocal(13, 8) },
        });

        Assert.Equal(new StatusSummary("Parked", "Until Sat, Jun 13 at 08:00", StatusSeverity.Info), summary);
    }

    [Fact]
    public void Parked_for_another_reason_tells_the_reason_and_the_next_start()
    {
        var frost = new PlannerInfo { RestrictedReason = RestrictedReason.Frost };

        var withNextStart = Summarize(TestMowers.Parked() with { Planner = frost with { NextStartTimestamp = MowerLocal(11, 8) } });
        var withoutNextStart = Summarize(TestMowers.Parked() with { Planner = frost });

        Assert.Equal(new StatusSummary("Parked", "Frost protection · Next start tomorrow at 08:00", StatusSeverity.Info), withNextStart);
        Assert.Equal(new StatusSummary("Parked", "Frost protection", StatusSeverity.Info), withoutNextStart);
    }

    [Theory]
    [InlineData(MowerActivity.GoingHome, "Going back to the station")]
    [InlineData(MowerActivity.Leaving, "Leaving the station")]
    [InlineData(MowerActivity.StoppedInGarden, "Stopped in the garden")]
    public void Moving_activities_are_named_with_the_next_start(MowerActivity activity, string title)
    {
        var attributes = WithNextStart(TestMowers.Parked(), MowerLocal(11, 8));
        var summary = Summarize(attributes with { Mower = new MowerStatus { Activity = activity, State = MowerState.InOperation } });

        Assert.Equal(new StatusSummary(title, "Next start tomorrow at 08:00", StatusSeverity.Ok), summary);
    }

    [Fact]
    public void Unknown_activity_shows_the_state()
    {
        var summary = Summarize(TestMowers.Parked() with { Mower = new MowerStatus { Activity = MowerActivity.Unknown, State = MowerState.InOperation } });

        Assert.Equal(new StatusSummary("In operation", "", StatusSeverity.Info), summary);
    }

    [Fact]
    public void Summary_is_translated()
    {
        using var culture = TestCulture.Use(TestCulture.French);
        var formatter = new MowerFormatter(ReswStrings.French);

        var parked = formatter.Summarize(TestMowers.Create(WithNextStart(TestMowers.Parked(), MowerLocal(11, 8))), s_now);
        var until = formatter.When(TestMowers.Local(2026, 6, 13, 8, 0), s_now);

        Assert.Equal(new StatusSummary("Garée", "Prochain départ demain à 08:00", StatusSeverity.Ok), parked);
        Assert.Equal("sam. 13 juin à 08:00", until);
    }

    // ----- Dates and durations -----

    [Theory]
    [InlineData(10, 8, 0, "today at 08:00")]
    [InlineData(11, 8, 0, "tomorrow at 08:00")]
    [InlineData(9, 22, 30, "yesterday at 22:30")]
    [InlineData(13, 8, 0, "Sat, Jun 13 at 08:00")]
    [InlineData(1, 7, 15, "Mon, Jun 1 at 07:15")]
    public void When_names_the_day(int day, int hour, int minute, string expected)
    {
        Assert.Equal(expected, _formatter.When(TestMowers.Local(2026, 6, day, hour, minute), s_now));
    }

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(5 * 60 + 59, "5 min ago")]
    [InlineData(3 * 3600 + 59 * 60, "3 h ago")]
    [InlineData(12 * 86400 + 3600, "12 d ago")]
    public void Relative_counts_whole_units(int secondsAgo, string expected)
    {
        Assert.Equal(expected, _formatter.Relative(s_now.AddSeconds(-secondsAgo), s_now));
    }

    [Theory]
    [InlineData(20, "1 min")]
    [InlineData(45 * 60, "45 min")]
    [InlineData(65 * 60, "1 h 05")]
    [InlineData(150 * 60, "2 h 30")]
    public void Duration_is_in_minutes_then_hours(int seconds, string expected)
    {
        Assert.Equal(expected, _formatter.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Days_of_a_slot()
    {
        var weekdays = new CalendarTask { Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true };
        var everyDay = weekdays with { Saturday = true, Sunday = true };

        Assert.Equal("Mon, Tue, Wed, Thu, Fri", _formatter.Days(weekdays));
        Assert.Equal("Every day", _formatter.Days(everyDay));
    }

    [Theory]
    [InlineData(480, 210, "08:00 – 11:30")]
    [InlineData(1320, 120, "22:00 – 00:00")]
    [InlineData(0, 1440, "00:00 – 00:00")]
    public void Time_range_of_a_slot(int start, int duration, string expected)
    {
        Assert.Equal(expected, MowerFormatter.TimeRange(new CalendarTask { Start = start, Duration = duration }));
    }

    [Fact]
    public void Statistics_units()
    {
        Assert.Equal("2,506 h", _formatter.Hours(2506 * 3600L + 1200));
        Assert.Equal("5,115 km", _formatter.Kilometers(5_115_200));
        Assert.Equal("62,944", MowerFormatter.Count(62_944));
        Assert.Equal("—", _formatter.Hours(null));
    }

    // ----- Resources -----

    [Fact]
    public void Every_displayed_enum_value_has_a_text_in_both_languages()
    {
        foreach (var strings in new[] { ReswStrings.English, ReswStrings.French })
        {
            AssertTexts<MowerActivity>(strings);
            AssertTexts<MowerState>(strings);
            AssertTexts<MowerMode>(strings);
            AssertTexts<InactiveReason>(strings);
            AssertTexts<RestrictedReason>(strings);
            AssertTexts<HeadlightMode>(strings);
            AssertTexts<MessageSeverity>(strings);
        }

        static void AssertTexts<TEnum>(IStrings strings) where TEnum : struct, Enum
        {
            foreach (var value in Enum.GetValues<TEnum>())
            {
                Assert.NotEqual($"{typeof(TEnum).Name}_{value}", strings.EnumText(value));
            }
        }
    }

    private StatusSummary Summarize(MowerAttributes attributes) => _formatter.Summarize(TestMowers.Create(attributes), s_now);

    private static long MowerLocal(int day, int hour, int minute = 0) =>
        TestMowers.MowerLocalMilliseconds(TestMowers.Local(2026, 6, day, hour, minute));

    private static MowerAttributes WithNextStart(MowerAttributes attributes, long nextStart) =>
        attributes with { Planner = attributes.Planner with { NextStartTimestamp = nextStart } };

    private static MowerAttributes Charging(MowerAttributes attributes) =>
        attributes with { Mower = attributes.Mower with { Activity = MowerActivity.Charging } };
}
