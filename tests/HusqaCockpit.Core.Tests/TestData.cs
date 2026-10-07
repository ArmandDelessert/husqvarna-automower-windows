using System.Text.Json.Nodes;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Tests;

internal static class TestData
{
    public const string FrontLawnId = "11111111-aaaa-4bbb-8ccc-000000000001";
    public const string BackGardenId = "11111111-aaaa-4bbb-8ccc-000000000002";

    public static TimeZoneInfo Zurich { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    public static string MowersJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mowers.json"));

    public static IReadOnlyList<JsonObject> MowerResources() =>
        JsonNode.Parse(MowersJson)!["data"]!.AsArray().Select(n => (JsonObject)n!.DeepClone()).ToList();

    public static MowerFleet LoadedFleet()
    {
        var fleet = new MowerFleet(Zurich);
        fleet.ApplySnapshot(MowerResources());
        return fleet;
    }

    public static Mower FrontLawn() => LoadedFleet().Find(FrontLawnId)!;

    /// <summary>Returns a copy of <paramref name="mower"/> with its status/metadata adjusted.</summary>
    public static Mower With(
        this Mower mower,
        MowerState? state = null,
        int? errorCode = null,
        bool? connected = null,
        MowerActivity? activity = null,
        int? battery = null,
        RestrictedReason? restrictedReason = null,
        OverrideAction? overrideAction = null,
        IReadOnlyList<CalendarTask>? schedule = null)
    {
        var attributes = mower.Attributes with
        {
            Mower = mower.Attributes.Mower with
            {
                State = state ?? mower.State,
                ErrorCode = errorCode ?? mower.ErrorCode,
                Activity = activity ?? mower.Activity,
            },
            Metadata = mower.Attributes.Metadata with { Connected = connected ?? mower.IsConnected },
            Battery = mower.Attributes.Battery with { BatteryPercent = battery ?? mower.BatteryPercent },
            Planner = mower.Attributes.Planner with
            {
                RestrictedReason = restrictedReason ?? mower.Attributes.Planner.RestrictedReason,
                Override = new PlannerOverride { Action = overrideAction ?? mower.Attributes.Planner.Override.Action },
            },
            Calendar = schedule is null ? mower.Attributes.Calendar : new CalendarInfo { Tasks = schedule },
        };
        return mower with { Attributes = attributes };
    }

    /// <summary>The alerts raised, in order, by each change of the sequence of states.</summary>
    public static List<MowerAlertKind> AlertKinds(params Mower[] states) =>
        states.Zip(states.Skip(1), AlertDetector.Detect).SelectMany(alerts => alerts.Select(a => a.Kind)).ToList();
}
