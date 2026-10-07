using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Fleet;

/// <summary>
/// Adds to the task alerts what <see cref="AlertDetector"/> cannot tell from two snapshots: why a task started, and
/// how long a finished one lasted. It remembers when each mower's task started (as seen by this application) and the
/// start commands the application sent.
/// </summary>
public sealed class TaskTracker(TimeProvider time)
{
    /// <summary>A task that starts within this time of a start command sent from the application is its doing.</summary>
    internal static readonly TimeSpan CommandWindow = TimeSpan.FromMinutes(2);

    /// <summary>The mower and this PC do not agree on the time to the second: a slot is "open" a little before it starts.</summary>
    internal static readonly TimeSpan ScheduleTolerance = TimeSpan.FromMinutes(5);

    /// <summary>A start older than this is stale (an end must have been missed): the duration is not reported.</summary>
    internal static readonly TimeSpan MaximumTaskDuration = TimeSpan.FromHours(24);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _taskStarts = [];
    private readonly Dictionary<string, DateTimeOffset> _startCommands = [];

    /// <summary>Records that the application has just sent a command that can start the mower.</summary>
    public void NoteStartCommand(string mowerId)
    {
        lock (_lock)
        {
            _startCommands[mowerId] = time.GetUtcNow();
        }
    }

    /// <summary>The alert with the information this tracker adds; other kinds are returned as they are.</summary>
    public MowerAlert Enrich(MowerAlert alert)
    {
        var now = time.GetUtcNow();
        var id = alert.Mower.Id;
        lock (_lock)
        {
            switch (alert.Kind)
            {
                case MowerAlertKind.TaskStarted:
                    _taskStarts[id] = now;
                    return alert with { StartReason = ReasonFor(alert.Mower, now) };

                case MowerAlertKind.TaskFinished:
                    TimeSpan? duration = _taskStarts.Remove(id, out var started) && now - started is { } elapsed
                        && elapsed >= TimeSpan.Zero && elapsed <= MaximumTaskDuration
                        ? elapsed
                        : null;
                    return alert with { Duration = duration };

                case MowerAlertKind.Disconnected:
                    // While the mower cannot be heard, its task may start or end unseen: do not time it.
                    _taskStarts.Remove(id);
                    return alert;

                default:
                    return alert;
            }
        }
    }

    private TaskStartReason ReasonFor(Mower mower, DateTimeOffset now)
    {
        if (_startCommands.TryGetValue(mower.Id, out var sent) && now - sent <= CommandWindow)
        {
            return TaskStartReason.Application;
        }
        if (mower.Attributes.Planner.Override.Action == OverrideAction.ForceMow)
        {
            return TaskStartReason.Manual;
        }
        return IsInsideScheduledSlot(mower, now) ? TaskStartReason.Schedule : TaskStartReason.Unknown;
    }

    private static bool IsInsideScheduledSlot(Mower mower, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, mower.TimeZone);
        var minuteOfDay = local.TimeOfDay.TotalMinutes;
        var tolerance = ScheduleTolerance.TotalMinutes;
        foreach (var slot in mower.Attributes.Calendar.Tasks)
        {
            if (slot.Days.Contains(local.DayOfWeek) && minuteOfDay >= slot.Start - tolerance && minuteOfDay < slot.Start + slot.Duration)
            {
                return true;
            }
        }
        return false;
    }
}
