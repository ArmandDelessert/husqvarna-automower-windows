using HusqaCockpit.Core.Fleet;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace HusqaCockpit.App.Services;

/// <summary>Windows toast notifications for mower alerts.</summary>
public sealed partial class NotificationService(ILogger<NotificationService> logger)
{
    private const string ActionKey = "action";
    private const string MowerKey = "mower";
    private const string OpenAction = "open";

    private bool _registered;

    /// <summary>The user clicked a notification. The argument is the mower id, if any. Raised on a background thread.</summary>
    public event EventHandler<string?>? OpenRequested;

    public void Initialize()
    {
        try
        {
            // The handler must be attached before registering.
            AppNotificationManager.Default.NotificationInvoked += (_, args) => HandleArguments(args.Arguments);
            AppNotificationManager.Default.Register(Loc.Get("AppName"), new Uri(AppPaths.IconPngPath));
            _registered = true;
        }
        catch (Exception ex)
        {
            LogUnavailable(logger, ex);
        }
    }

    /// <summary>Handles a launch caused by a click on a notification while the app was not running.</summary>
    public void HandleActivation(AppNotificationActivatedEventArgs args) => HandleArguments(args.Arguments);

    public void Show(MowerAlert alert, AppSettings settings)
    {
        var enabled = alert.Kind switch
        {
            MowerAlertKind.Error or MowerAlertKind.Alarm => settings.NotifyErrors,
            MowerAlertKind.ErrorCleared => settings.NotifyRecoveries,
            MowerAlertKind.Stopped => settings.NotifyStopped,
            MowerAlertKind.Disconnected or MowerAlertKind.Reconnected => settings.NotifyConnectivity,
            _ => false,
        };
        if (!enabled)
        {
            return;
        }

        var name = alert.Mower.Name;
        var (title, body) = alert.Kind switch
        {
            MowerAlertKind.Alarm => (Loc.Format("Notification_AlarmTitle", name), Loc.ErrorCode(alert.ErrorCode)),
            MowerAlertKind.Error => (Loc.Format("Notification_ErrorTitle", name), Loc.ErrorCode(alert.ErrorCode)),
            MowerAlertKind.ErrorCleared => (Loc.Format("Notification_ClearedTitle", name), Loc.Format("Notification_ClearedBody", Loc.ErrorCode(alert.ErrorCode))),
            MowerAlertKind.Stopped => (Loc.Format("Notification_StoppedTitle", name), Loc.Get("Notification_StoppedBody")),
            MowerAlertKind.Disconnected => (Loc.Format("Notification_DisconnectedTitle", name), Loc.Get("Notification_DisconnectedBody")),
            _ => (Loc.Format("Notification_ReconnectedTitle", name), Loc.Get("Notification_ReconnectedBody")),
        };

        var builder = new AppNotificationBuilder()
            .AddArgument(ActionKey, OpenAction)
            .AddArgument(MowerKey, alert.Mower.Id)
            .AddText(title)
            .AddText(body)
            .AddButton(new AppNotificationButton(Loc.Get("Notification_Open"))
                .AddArgument(ActionKey, OpenAction)
                .AddArgument(MowerKey, alert.Mower.Id));
        if (alert.Kind == MowerAlertKind.Alarm)
        {
            builder.SetScenario(AppNotificationScenario.Urgent);
        }

        Show(builder.BuildNotification(), tag: alert.Mower.Id);
    }

    public void ShowInfo(string title, string body) =>
        Show(new AppNotificationBuilder().AddArgument(ActionKey, OpenAction).AddText(title).AddText(body).BuildNotification(), tag: "info");

    private void Show(AppNotification notification, string tag)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            // One notification per mower: a newer alert replaces the previous one in the Action Center.
            notification.Tag = tag.Length > 64 ? tag[..64] : tag;
            notification.Group = "mowers";
            AppNotificationManager.Default.Show(notification);
            LogShown(logger, notification.Id, notification.Tag);
        }
        catch (Exception ex)
        {
            LogShowFailed(logger, ex);
        }
    }

    private void HandleArguments(IDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue(ActionKey, out var action) && action == OpenAction)
        {
            OpenRequested?.Invoke(this, arguments.TryGetValue(MowerKey, out var mowerId) ? mowerId : null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications unavailable")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification shown (id {Id}, tag {Tag})")]
    private static partial void LogShown(ILogger logger, uint id, string tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not show notification")]
    private static partial void LogShowFailed(ILogger logger, Exception exception);
}
