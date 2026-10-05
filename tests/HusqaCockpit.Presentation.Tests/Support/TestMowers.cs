using System.Text.Json;
using System.Text.Json.Nodes;
using HusqaCockpit.Core.Json;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Presentation.Tests.Support;

/// <summary>Fake mowers (no real names, serial numbers or positions) and fixed dates in the local time zone.</summary>
internal static class TestMowers
{
    /// <summary>Mowers run in the PC's time zone, as the app assumes.</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.Local;

    /// <summary>Noon on Wednesday 10 June 2026, local time: far from any daylight-saving change.</summary>
    public static DateTimeOffset Now { get; } = Local(2026, 6, 10, 12, 0);

    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var wallClock = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, Zone.GetUtcOffset(wallClock));
    }

    /// <summary>The API's timestamps in "mower local time": milliseconds of the wall clock, as if it were UTC.</summary>
    public static long MowerLocalMilliseconds(DateTimeOffset local) =>
        new DateTimeOffset(local.DateTime, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>A connected mower parked in its station, following its schedule, last heard of 5 minutes ago.</summary>
    public static MowerAttributes Parked(string name = "Alpha") => new()
    {
        System = new SystemInfo { Name = name, Model = "HUSQVARNA AUTOMOWER® 450X", SerialNumber = 123 },
        Battery = new BatteryInfo { BatteryPercent = 100 },
        Mower = new MowerStatus { Mode = MowerMode.MainArea, Activity = MowerActivity.ParkedInCs, State = MowerState.Restricted },
        Planner = new PlannerInfo { RestrictedReason = RestrictedReason.WeekSchedule },
        Metadata = new MetadataInfo { Connected = true, StatusTimestamp = Now.AddMinutes(-5).ToUnixTimeMilliseconds() },
    };

    public static MowerAttributes Mowing(string name = "Alpha") => Parked(name) with
    {
        Mower = new MowerStatus { Mode = MowerMode.MainArea, Activity = MowerActivity.Mowing, State = MowerState.InOperation },
        Planner = new PlannerInfo { RestrictedReason = RestrictedReason.None },
    };

    public static MowerAttributes InError(int code, string name = "Alpha") => Parked(name) with
    {
        Mower = new MowerStatus { Activity = MowerActivity.StoppedInGarden, State = MowerState.Error, ErrorCode = code },
    };

    public static Mower Create(MowerAttributes attributes, string id = "mower-1") =>
        new() { Id = id, Attributes = attributes, TimeZone = Zone };

    /// <summary>The JSON:API resource of GET /mowers for <paramref name="attributes"/>.</summary>
    public static JsonObject Resource(string id, MowerAttributes attributes) => new()
    {
        ["id"] = id,
        ["type"] = "mower",
        ["attributes"] = JsonSerializer.SerializeToNode(attributes, AutomowerJson.Options),
    };
}
