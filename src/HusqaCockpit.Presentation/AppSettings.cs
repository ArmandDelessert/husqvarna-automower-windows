using System.Text.Json.Serialization;

namespace HusqaCockpit.Presentation;

/// <summary>Which notifications the user wants about the mowing tasks of the mowers.</summary>
public enum TaskNotificationMode
{
    /// <summary>No notification when a mower starts or finishes mowing.</summary>
    None,

    /// <summary>A notification when a mower finishes mowing.</summary>
    EndOnly,

    /// <summary>A notification when a mower starts mowing and another when it finishes.</summary>
    StartAndEnd,
}

/// <summary>User preferences. The app stores them as JSON in %LocalAppData%\HusqA Cockpit\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"" follows Windows; otherwise a language tag such as "fr-FR" or "en-US".</summary>
    public string Language { get; set; } = "";

    public bool NotifyErrors { get; set; } = true;
    public bool NotifyRecoveries { get; set; } = true;
    public bool NotifyStopped { get; set; } = true;
    public bool NotifyConnectivity { get; set; } = true;

    /// <summary>Start and end of mowing: off by default, so that an update does not add notifications nobody asked for.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TaskNotificationMode>))]
    public TaskNotificationMode TaskNotifications { get; set; } = TaskNotificationMode.None;

    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Set once the user has been told the app keeps running after the window is closed.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>REST refresh interval used when real-time events are unavailable.</summary>
    public int PollingIntervalMinutes { get; set; } = 10;

    /// <summary>Month ("yyyy-MM") the request counter refers to.</summary>
    public string QuotaMonth { get; set; } = "";

    /// <summary>API requests sent from this computer during <see cref="QuotaMonth"/>.</summary>
    public int QuotaRequests { get; set; }

    public WindowBounds? Window { get; set; }
}

public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);
