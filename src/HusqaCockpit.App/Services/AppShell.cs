using System.Diagnostics;
using HusqaCockpit.Presentation;
using Microsoft.UI.Xaml.Controls;

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

    public int CountLogsOlderThan(int days) => FileLoggerProvider.CountOlderThan(AppPaths.LogsFolder, days);

    public void SetLogRetention(int days) => app.LogFile.RetentionDays = days;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = app.Window.Content.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);
        Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
    }
}
