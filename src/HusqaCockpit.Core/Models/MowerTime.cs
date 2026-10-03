namespace HusqaCockpit.Core.Models;

/// <summary>
/// The Automower API expresses most timestamps (next start, error time, messages) as
/// "epoch" values computed from the mower's <em>local</em> wall-clock time rather than UTC.
/// These helpers reinterpret them in the mower's time zone.
/// </summary>
public static class MowerTime
{
    // Values above this are milliseconds; below are seconds (year 3000 in seconds).
    private const long MillisecondsThreshold = 32_503_680_000;

    public static DateTimeOffset? FromMowerLocal(long timestamp, TimeZoneInfo zone)
    {
        if (timestamp <= 0)
        {
            return null;
        }

        var wallClock = timestamp > MillisecondsThreshold
            ? DateTime.UnixEpoch.AddMilliseconds(timestamp)
            : DateTime.UnixEpoch.AddSeconds(timestamp);
        wallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        // A wall-clock time inside a DST "spring forward" gap does not exist; nudge it past the gap.
        if (zone.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddHours(1);
        }

        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
    }

    public static DateTimeOffset? FromUtcMilliseconds(long milliseconds) =>
        milliseconds <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
