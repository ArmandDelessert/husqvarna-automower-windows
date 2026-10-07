using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Fleet;

public enum MowerAlertKind
{
    /// <summary>The mower reported an error (or a different error than before).</summary>
    Error,

    /// <summary>A theft-protection alarm (codes 69–74).</summary>
    Alarm,

    /// <summary>The mower recovered from an error.</summary>
    ErrorCleared,

    /// <summary>The mower stopped and requires a manual action (e.g. STOP button pressed).</summary>
    Stopped,

    /// <summary>The mower lost its connection to the Husqvarna cloud.</summary>
    Disconnected,

    /// <summary>The mower is reachable again.</summary>
    Reconnected,

    /// <summary>The mower started a mowing task (see <see cref="AlertDetector"/> for what counts as one).</summary>
    TaskStarted,

    /// <summary>The mower finished its mowing task.</summary>
    TaskFinished,
}

/// <summary>Why a mowing task started, as far as it can be told.</summary>
public enum TaskStartReason
{
    /// <summary>Not known: the task did not start in a scheduled slot, and no override or command explains it.</summary>
    Unknown,

    /// <summary>The weekly schedule: the task started inside one of its time slots.</summary>
    Schedule,

    /// <summary>Forced by someone else (another app, a voice assistant, the mower's own keypad): the planner is in "force mow".</summary>
    Manual,

    /// <summary>A command sent from this application a moment ago.</summary>
    Application,
}

/// <param name="Kind">What happened.</param>
/// <param name="Mower">The mower after the change.</param>
/// <param name="ErrorCode">For errors and recoveries: the error code.</param>
/// <param name="StartReason">For <see cref="MowerAlertKind.TaskStarted"/>: why it started (filled in by <see cref="TaskTracker"/>).</param>
/// <param name="Duration">For <see cref="MowerAlertKind.TaskFinished"/>: how long the task lasted, when its start was seen (filled in by <see cref="TaskTracker"/>).</param>
public sealed record MowerAlert(
    MowerAlertKind Kind,
    Mower Mower,
    int ErrorCode = 0,
    TaskStartReason StartReason = TaskStartReason.Unknown,
    TimeSpan? Duration = null);

/// <summary>
/// Turns state transitions into user-facing alerts.
/// <para>
/// <b>Start and end of a mowing task.</b> The API describes <c>state</c> <c>IN_OPERATION</c> as "see the activity" (the
/// mower is working) and <c>RESTRICTED</c> as "the mower can currently not mow, due to the week calendar or an override
/// park". A mower that goes back to its station because its battery is low is still <c>IN_OPERATION</c> (activity
/// <c>GOING_HOME</c>, then <c>CHARGING</c> "due to low battery", then <c>LEAVING</c>, then <c>MOWING</c> again), whatever
/// its battery level, so the activity and the battery cannot tell a recharge from the end of the task. The state can:
/// </para>
/// <list type="bullet">
/// <item>a task <b>starts</b> when the state goes from <c>RESTRICTED</c> to <c>IN_OPERATION</c>;</item>
/// <item>a task <b>ends</b> when it goes from <c>IN_OPERATION</c> to <c>RESTRICTED</c>: the schedule slot is over, a
/// park was requested, or a daily limit, frost or sensor restriction applies.</item>
/// </list>
/// <para>
/// Pausing, a stop, an error or switching off are neither: they interrupt the task (and the following resume does not
/// start one). Nothing is reported when either side of the change is not connected, because the state last seen may be
/// old (the mower came back after missing a start or an end), nor for the first sight of a mower (app start-up).
/// </para>
/// </summary>
public static class AlertDetector
{
    public static IReadOnlyList<MowerAlert> Detect(Mower? previous, Mower current)
    {
        var alerts = new List<MowerAlert>();

        if (previous is null)
        {
            // First sight of this mower (app start): only report ongoing problems.
            if (current.HasError)
            {
                alerts.Add(ErrorAlert(current));
            }
            return alerts;
        }

        if (current.HasError && (!previous.HasError || previous.ErrorCode != current.ErrorCode))
        {
            alerts.Add(ErrorAlert(current));
        }
        else if (previous.HasError && !current.HasError)
        {
            alerts.Add(new MowerAlert(MowerAlertKind.ErrorCleared, current, previous.ErrorCode));
        }

        if (!current.HasError && current.State == MowerState.Stopped && previous.State != MowerState.Stopped)
        {
            alerts.Add(new MowerAlert(MowerAlertKind.Stopped, current));
        }

        if (previous.IsConnected && !current.IsConnected)
        {
            alerts.Add(new MowerAlert(MowerAlertKind.Disconnected, current));
        }
        else if (!previous.IsConnected && current.IsConnected)
        {
            alerts.Add(new MowerAlert(MowerAlertKind.Reconnected, current));
        }

        if (previous.IsConnected && current.IsConnected && !current.HasError)
        {
            if (previous.State == MowerState.Restricted && current.State == MowerState.InOperation)
            {
                alerts.Add(new MowerAlert(MowerAlertKind.TaskStarted, current));
            }
            else if (previous.State == MowerState.InOperation && current.State == MowerState.Restricted)
            {
                alerts.Add(new MowerAlert(MowerAlertKind.TaskFinished, current));
            }
        }

        return alerts;
    }

    private static MowerAlert ErrorAlert(Mower mower) =>
        new(mower.IsAlarm ? MowerAlertKind.Alarm : MowerAlertKind.Error, mower, mower.ErrorCode);
}
