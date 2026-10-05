using System.Diagnostics;
using HusqaCockpit.Presentation;

namespace HusqaCockpit.App.Services;

/// <summary>The application around the view models: start with Windows, notifications, restart, logs.</summary>
public sealed class AppShell(App app) : IAppShell
{
    public bool StartsWithWindows
    {
        get => StartupRegistration.IsEnabled;
        set => StartupRegistration.SetEnabled(value);
    }

    public void ShowNotification(string title, string body) => app.Notifications.ShowInfo(title, body);

    public void Restart() => app.Restart();

    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);
        Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
    }
}
