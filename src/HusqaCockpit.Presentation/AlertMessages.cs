using HusqaCockpit.Core.Fleet;

namespace HusqaCockpit.Presentation;

/// <summary>
/// Which alerts become Windows notifications, and what they say. The app only shows the result
/// (<c>NotificationService</c>), so that both decisions can be unit-tested.
/// </summary>
public sealed class AlertMessages(IStrings strings)
{
    private readonly MowerFormatter _formatter = new(strings);

    /// <summary>Whether the user's preferences ask for a notification about this kind of alert.</summary>
    public static bool IsEnabled(MowerAlertKind kind, AppSettings settings) => kind switch
    {
        MowerAlertKind.Error or MowerAlertKind.Alarm => settings.NotifyErrors,
        MowerAlertKind.ErrorCleared => settings.NotifyRecoveries,
        MowerAlertKind.Stopped => settings.NotifyStopped,
        MowerAlertKind.Disconnected or MowerAlertKind.Reconnected => settings.NotifyConnectivity,
        MowerAlertKind.TaskStarted => settings.TaskNotifications == TaskNotificationMode.StartAndEnd,
        MowerAlertKind.TaskFinished => settings.TaskNotifications is TaskNotificationMode.EndOnly or TaskNotificationMode.StartAndEnd,
        _ => false,
    };

    /// <summary>The title and the text of the notification.</summary>
    public (string Title, string Body) Describe(MowerAlert alert)
    {
        var name = alert.Mower.Name;
        return alert.Kind switch
        {
            MowerAlertKind.Alarm => (strings.Format("Notification_AlarmTitle", name), strings.ErrorCode(alert.ErrorCode)),
            MowerAlertKind.Error => (strings.Format("Notification_ErrorTitle", name), strings.ErrorCode(alert.ErrorCode)),
            MowerAlertKind.ErrorCleared => (strings.Format("Notification_ClearedTitle", name), strings.Format("Notification_ClearedBody", strings.ErrorCode(alert.ErrorCode))),
            MowerAlertKind.Stopped => (strings.Format("Notification_StoppedTitle", name), strings.Text("Notification_StoppedBody")),
            MowerAlertKind.Disconnected => (strings.Format("Notification_DisconnectedTitle", name), strings.Text("Notification_DisconnectedBody")),
            MowerAlertKind.Reconnected => (strings.Format("Notification_ReconnectedTitle", name), strings.Text("Notification_ReconnectedBody")),
            MowerAlertKind.TaskStarted => (strings.Format("Notification_TaskStartedTitle", name), StartedBody(alert.StartReason)),
            MowerAlertKind.TaskFinished => (strings.Format("Notification_TaskFinishedTitle", name), FinishedBody(alert.Duration)),
            _ => throw new ArgumentOutOfRangeException(nameof(alert), alert.Kind, "Unknown kind of alert."),
        };
    }

    private string StartedBody(TaskStartReason reason) => strings.Text(reason switch
    {
        TaskStartReason.Schedule => "Notification_TaskStartedSchedule",
        TaskStartReason.Application => "Notification_TaskStartedApplication",
        TaskStartReason.Manual => "Notification_TaskStartedManual",
        _ => "Notification_TaskStartedUnknown",
    });

    private string FinishedBody(TimeSpan? duration) => duration is { } elapsed
        ? strings.Format("Notification_TaskFinishedDuration", _formatter.Duration(elapsed))
        : strings.Text("Notification_TaskFinishedUnknown");
}
