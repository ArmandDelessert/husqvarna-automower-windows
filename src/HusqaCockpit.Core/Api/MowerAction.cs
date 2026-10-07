namespace HusqaCockpit.Core.Api;

/// <summary>A control command accepted by POST /mowers/{id}/actions.</summary>
public sealed record MowerAction
{
    private MowerAction(string type, int? durationMinutes = null)
    {
        Type = type;
        DurationMinutes = durationMinutes;
    }

    public string Type { get; }
    public int? DurationMinutes { get; }

    /// <summary>Whether the command can make a parked mower leave and mow: "Start", and "ResumeSchedule" inside a time slot.</summary>
    public bool CanStartMowing => Type is "Start" or "ResumeSchedule";

    /// <summary>Mow for the given duration, overriding the schedule.</summary>
    public static MowerAction Start(TimeSpan duration) => new("Start", ToMinutes(duration));

    /// <summary>Stop where it is (the mower stays in the garden).</summary>
    public static MowerAction Pause() => new("Pause");

    /// <summary>Drop any override and follow the weekly schedule again.</summary>
    public static MowerAction ResumeSchedule() => new("ResumeSchedule");

    /// <summary>Go to the charging station and stay there for the given duration.</summary>
    public static MowerAction Park(TimeSpan duration) => new("Park", ToMinutes(duration));

    public static MowerAction ParkUntilNextSchedule() => new("ParkUntilNextSchedule");

    public static MowerAction ParkUntilFurtherNotice() => new("ParkUntilFurtherNotice");

    private static int ToMinutes(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.FromMinutes(1));
        return (int)Math.Round(duration.TotalMinutes);
    }
}
