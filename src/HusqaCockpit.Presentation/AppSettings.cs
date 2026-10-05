namespace HusqaCockpit.Presentation;

/// <summary>User preferences. The app stores them as JSON in %LOCALAPPDATA%\HusqA Cockpit\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"" follows Windows; otherwise a language tag such as "fr-FR" or "en-US".</summary>
    public string Language { get; set; } = "";

    public bool NotifyErrors { get; set; } = true;
    public bool NotifyRecoveries { get; set; } = true;
    public bool NotifyStopped { get; set; } = true;
    public bool NotifyConnectivity { get; set; } = true;

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
