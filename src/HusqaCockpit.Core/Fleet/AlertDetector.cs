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
}

public sealed record MowerAlert(MowerAlertKind Kind, Mower Mower, int ErrorCode = 0);

/// <summary>Turns state transitions into user-facing alerts.</summary>
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

        return alerts;
    }

    private static MowerAlert ErrorAlert(Mower mower) =>
        new(mower.IsAlarm ? MowerAlertKind.Alarm : MowerAlertKind.Error, mower, mower.ErrorCode);
}
