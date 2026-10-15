namespace HusqaCockpit.Presentation;

/// <summary>What the view models ask of the application around them: its place in Windows and its lifecycle.</summary>
public interface IAppShell
{
    /// <summary>Whether the app starts with Windows, in the notification area.</summary>
    bool StartsWithWindows { get; set; }

    void ShowNotification(string title, string body);

    /// <summary>Starts a new instance of the app, then quits this one (e.g. to apply a new language).</summary>
    void Restart();

    void OpenLogsFolder();

    void OpenEventsFolder();

    /// <summary>How many log files are older than <paramref name="days"/> days, and so go if that becomes the retention.</summary>
    int CountLogsOlderThan(int days);

    /// <summary>Keeps the log files for <paramref name="days"/> days (0 for ever): the older ones are deleted at once.</summary>
    void SetLogRetention(int days);

    /// <summary>Asks a yes/no question to the user; true for yes.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
}
