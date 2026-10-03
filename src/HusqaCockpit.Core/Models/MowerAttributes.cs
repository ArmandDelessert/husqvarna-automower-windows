using System.Text.Json.Serialization;

namespace HusqaCockpit.Core.Models;

// Mirrors the "attributes" object of GET /mowers (Automower Connect API v1).
// Every member has a default so that partial payloads still deserialize.

public sealed record MowerAttributes
{
    public SystemInfo System { get; init; } = new();
    public BatteryInfo Battery { get; init; } = new();
    public MowerCapabilities Capabilities { get; init; } = new();
    public MowerStatus Mower { get; init; } = new();
    public CalendarInfo Calendar { get; init; } = new();
    public PlannerInfo Planner { get; init; } = new();
    public MetadataInfo Metadata { get; init; } = new();
    public IReadOnlyList<GeoPosition> Positions { get; init; } = [];
    public MowerSettings Settings { get; init; } = new();
    public MowerStatistics Statistics { get; init; } = new();
}

public sealed record SystemInfo
{
    public string Name { get; init; } = "";
    public string Model { get; init; } = "";
    public long SerialNumber { get; init; }
}

public sealed record BatteryInfo
{
    public int BatteryPercent { get; init; }

    /// <summary>Remaining charging time in seconds (0 when not charging).</summary>
    public int RemainingChargingTime { get; init; }
}

public sealed record MowerCapabilities
{
    public bool Headlights { get; init; }
    public bool WorkAreas { get; init; }
    public bool Position { get; init; }
    public bool CanConfirmError { get; init; }
    public bool StayOutZones { get; init; }
}

public sealed record MowerStatus
{
    public MowerMode Mode { get; init; }
    public MowerActivity Activity { get; init; }
    public InactiveReason InactiveReason { get; init; }
    public MowerState State { get; init; }
    public int ErrorCode { get; init; }

    /// <summary>Milliseconds since 1970-01-01, expressed in the mower's local time (not UTC). 0 when no error.</summary>
    public long ErrorCodeTimestamp { get; init; }

    public bool IsErrorConfirmable { get; init; }
    public long? WorkAreaId { get; init; }
}

public sealed record CalendarInfo
{
    public IReadOnlyList<CalendarTask> Tasks { get; init; } = [];
}

public sealed record CalendarTask
{
    /// <summary>Start time in minutes after midnight.</summary>
    public int Start { get; init; }

    /// <summary>Duration in minutes.</summary>
    public int Duration { get; init; }

    public bool Monday { get; init; }
    public bool Tuesday { get; init; }
    public bool Wednesday { get; init; }
    public bool Thursday { get; init; }
    public bool Friday { get; init; }
    public bool Saturday { get; init; }
    public bool Sunday { get; init; }
    public long? WorkAreaId { get; init; }

    /// <summary>Convenience view of the day flags; not part of the API payload.</summary>
    [JsonIgnore]
    public IEnumerable<DayOfWeek> Days
    {
        get
        {
            if (Monday) yield return DayOfWeek.Monday;
            if (Tuesday) yield return DayOfWeek.Tuesday;
            if (Wednesday) yield return DayOfWeek.Wednesday;
            if (Thursday) yield return DayOfWeek.Thursday;
            if (Friday) yield return DayOfWeek.Friday;
            if (Saturday) yield return DayOfWeek.Saturday;
            if (Sunday) yield return DayOfWeek.Sunday;
        }
    }
}

public sealed record PlannerInfo
{
    /// <summary>Milliseconds since 1970-01-01, expressed in the mower's local time (not UTC). 0 when unknown.</summary>
    public long NextStartTimestamp { get; init; }

    public PlannerOverride Override { get; init; } = new();
    public RestrictedReason RestrictedReason { get; init; }
    public int? ExternalReason { get; init; }
}

public sealed record PlannerOverride
{
    public OverrideAction Action { get; init; }
}

public sealed record MetadataInfo
{
    public bool Connected { get; init; }

    /// <summary>Milliseconds since 1970-01-01 UTC of the last status received by the cloud.</summary>
    public long StatusTimestamp { get; init; }
}

public sealed record GeoPosition
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed record MowerSettings
{
    public int? CuttingHeight { get; init; }
    public HeadlightSettings Headlight { get; init; } = new();
}

public sealed record HeadlightSettings
{
    public HeadlightMode Mode { get; init; }
}

/// <summary>Lifetime statistics. Durations are in seconds, distances in meters.</summary>
public sealed record MowerStatistics
{
    public long? CuttingBladeUsageTime { get; init; }
    public long? DownTime { get; init; }
    public long? UpTime { get; init; }
    public long? NumberOfChargingCycles { get; init; }
    public long? NumberOfCollisions { get; init; }
    public long? TotalChargingTime { get; init; }
    public long? TotalCuttingTime { get; init; }
    public long? TotalDriveDistance { get; init; }
    public long? TotalRunningTime { get; init; }
    public long? TotalSearchingTime { get; init; }
}

public sealed record MowerMessage
{
    /// <summary>Seconds since 1970-01-01, expressed in the mower's local time (not UTC).</summary>
    public long Time { get; init; }

    public int Code { get; init; }
    public MessageSeverity Severity { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}
