namespace HusqaCockpit.Core.Models;

/// <summary>An immutable snapshot of one mower, with the API's raw attributes and convenience accessors.</summary>
public sealed record Mower
{
    public required string Id { get; init; }
    public required MowerAttributes Attributes { get; init; }

    /// <summary>Time zone the mower runs in; used to interpret its local-time timestamps.</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    public string Name => Attributes.System.Name;
    public string Model => Attributes.System.Model;
    public int BatteryPercent => Attributes.Battery.BatteryPercent;
    public MowerActivity Activity => Attributes.Mower.Activity;
    public MowerState State => Attributes.Mower.State;
    public MowerMode Mode => Attributes.Mower.Mode;
    public int ErrorCode => Attributes.Mower.ErrorCode;
    public bool IsConnected => Attributes.Metadata.Connected;

    public bool HasError =>
        ErrorCode != 0 || State is MowerState.Error or MowerState.FatalError or MowerState.ErrorAtPowerUp;

    /// <summary>Theft-protection alarm codes (switched off, stopped, lifted, tilted, moving, outside geofence).</summary>
    public bool IsAlarm => IsAlarmCode(ErrorCode);

    public bool CanConfirmError => Attributes.Capabilities.CanConfirmError && Attributes.Mower.IsErrorConfirmable;

    public DateTimeOffset? ErrorTime => MowerTime.FromMowerLocal(Attributes.Mower.ErrorCodeTimestamp, TimeZone);
    public DateTimeOffset? NextStart => MowerTime.FromMowerLocal(Attributes.Planner.NextStartTimestamp, TimeZone);
    public DateTimeOffset? LastStatusTime => MowerTime.FromUtcMilliseconds(Attributes.Metadata.StatusTimestamp);
    public GeoPosition? LastPosition => Attributes.Positions.Count > 0 ? Attributes.Positions[0] : null;

    public static bool IsAlarmCode(int code) => code is >= 69 and <= 74;
}
