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
}
