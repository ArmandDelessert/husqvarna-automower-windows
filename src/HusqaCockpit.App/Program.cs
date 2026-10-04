using System.Globalization;
using HusqaCockpit.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace HusqaCockpit.App;

public static class Program
{
    private const string InstanceKey = "HusqaCockpit.Main";

    /// <summary>Command-line switch used by the "start with Windows" entry: start in the notification area only.</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>Command-line switch used when the app restarts itself: "--wait-for-exit=PID" waits for the old process to end.</summary>
    public const string WaitForExitArgument = "--wait-for-exit";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        WaitForPreviousInstance(args);

        // Only one instance: a second launch (or a click on a notification) is forwarded to the running one.
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        ApplyLanguage(AppSettingsStore.Load().Language);

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(startMinimized: args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase));
        });
        return 0;
    }

    /// <summary>When restarting, the old instance must be gone first: it owns the single-instance key and the tray icon.</summary>
    private static void WaitForPreviousInstance(string[] args)
    {
        var prefix = WaitForExitArgument + "=";
        var argument = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null || !int.TryParse(argument[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            return;
        }

        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    /// <summary>Applies the language chosen in the settings (empty = follow Windows). Must run before any UI is created.</summary>
    private static void ApplyLanguage(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (Exception ex) when (ex is ArgumentException or CultureNotFoundException or System.Runtime.InteropServices.COMException)
        {
            // Unknown language: keep the Windows default.
        }
    }
}
