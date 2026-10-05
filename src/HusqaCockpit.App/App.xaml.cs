using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HusqaCockpit.App.Services;
using HusqaCockpit.App.ViewModels;
using HusqaCockpit.Core.Fleet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace HusqaCockpit.App;

[SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The application object lives as long as the process; Quit() disposes the tray icon before exiting.")]
public partial class App : Application
{
    private static readonly TimeSpan s_quotaSaveDelay = TimeSpan.FromSeconds(5);

    private readonly bool _startMinimized;
    private ILoggerFactory _loggers = null!;
    // Exceptions raised before OnLaunched sets up the log file are not recorded.
    private ILogger _logger = NullLogger.Instance;
    private DispatcherQueue _dispatcher = null!;
    private DispatcherQueueTimer _quotaSaveTimer = null!;
    private TrayIcon? _tray;
    private bool _trayShowsAlert;

    public App(bool startMinimized)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        UnhandledException += (_, e) => LogUnhandledUiException(_logger, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogUnhandledException(_logger, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnobservedTaskException(_logger, e.Exception);
            e.SetObserved();
        };
    }

    public static new App Current => (App)Application.Current;

    public AppSettings Settings { get; private set; } = null!;
    public CredentialStore Credentials { get; private set; } = null!;
    public CockpitHost Host { get; private set; } = null!;
    public NotificationService Notifications { get; private set; } = null!;
    public DashboardViewModel Dashboard { get; private set; } = null!;
    public MainWindow Window { get; private set; } = null!;

    /// <summary>True once the user chose to quit (as opposed to closing the window to the tray).</summary>
    public bool IsExiting { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _loggers = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(new FileLoggerProvider(AppPaths.LogsFolder, LogLevel.Information)));
        _logger = _loggers.CreateLogger<App>();
        LogStarting(_logger, CultureInfo.CurrentUICulture.Name);

        Settings = AppSettingsStore.Load();
        _quotaSaveTimer = _dispatcher.CreateTimer();
        _quotaSaveTimer.Interval = s_quotaSaveDelay;
        _quotaSaveTimer.IsRepeating = false;
        _quotaSaveTimer.Tick += (_, _) => AppSettingsStore.Save(Settings);
        Credentials = new CredentialStore();
        Host = new CockpitHost(Credentials, Settings, _loggers);
        Host.RequestSent += (_, _) => _dispatcher.TryEnqueue(CountRequest);
        Host.Fleet.MowerChanged += OnMowerChanged;

        Notifications = new NotificationService(_loggers.CreateLogger<NotificationService>());
        Notifications.OpenRequested += (_, mowerId) => _dispatcher.TryEnqueue(() => ShowWindow(mowerId));
        Notifications.Initialize();

        Dashboard = new DashboardViewModel(Host, _dispatcher);
        Dashboard.SummaryChanged += (_, _) => UpdateTray();

        // A second launch of the exe is redirected here by Program.Main.
        AppInstance.GetCurrent().Activated += (_, e) => _dispatcher.TryEnqueue(() => OnActivated(e));

        Window = new MainWindow();
        CreateTrayIcon();

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activation.Kind == ExtendedActivationKind.AppNotification)
        {
            Notifications.HandleActivation((AppNotificationActivatedEventArgs)activation.Data);
        }
        else if (!_startMinimized || !Host.HasCredentials)
        {
            ShowWindow(null);
        }

        _ = StartMonitoringAsync();
    }

    /// <summary>Brings the main window to the front, optionally on a mower's detail page.</summary>
    public void ShowWindow(string? mowerId)
    {
        Window.ShowAndActivate();
        if (mowerId is not null)
        {
            Window.ShowMower(mowerId);
        }
    }

    /// <summary>Quits the application (the tray icon disappears).</summary>
    public async void Quit()
    {
        if (IsExiting)
        {
            return;
        }
        IsExiting = true;
        LogExiting(_logger);
        SavePendingQuota();
        _tray?.Dispose();
        _tray = null;
        Window.AppWindow.Hide();
        try
        {
            await Host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            LogShutdownFailed(_logger, ex);
        }
        _loggers.Dispose();
        Exit();
    }

    /// <summary>Starts a new instance of the app, then quits this one (e.g. to apply a new language).</summary>
    public void Restart()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Environment.ProcessPath!, $"{Program.WaitForExitArgument}={Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogRestartFailed(_logger, ex);
            return;
        }
        Quit();
    }

    /// <summary>Called when the main window is closed while the app keeps running in the tray.</summary>
    public void OnWindowHiddenToTray()
    {
        if (Settings.TrayHintShown)
        {
            return;
        }
        Settings.TrayHintShown = true;
        AppSettingsStore.Save(Settings);
        Notifications.ShowInfo(Loc.Get("Notification_TrayHintTitle"), Loc.Get("Notification_TrayHintBody"));
    }

    private async Task StartMonitoringAsync()
    {
        try
        {
            await Host.RestartAsync();
        }
        catch (Exception ex)
        {
            LogMonitoringStartFailed(_logger, ex);
        }
    }

    private void OnActivated(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.AppNotification)
        {
            Notifications.HandleActivation((AppNotificationActivatedEventArgs)args.Data);
        }
        else
        {
            ShowWindow(null);
        }
    }

    private void OnMowerChanged(object? sender, MowerChangedEventArgs e)
    {
        foreach (var alert in AlertDetector.Detect(e.Previous, e.Current))
        {
            LogAlert(_logger, alert.Kind, alert.Mower.Name, alert.ErrorCode);
            Notifications.Show(alert, Settings);
        }
    }

    private void CountRequest()
    {
        var month = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (Settings.QuotaMonth != month)
        {
            Settings.QuotaMonth = month;
            Settings.QuotaRequests = 0;
        }
        Settings.QuotaRequests++;

        // Requests come in bursts (a refresh, a command for every mower): write the file once a burst is over,
        // not on every request. Quit() writes what is still pending.
        if (!_quotaSaveTimer.IsRunning)
        {
            _quotaSaveTimer.Start();
        }
    }

    private void SavePendingQuota()
    {
        if (_quotaSaveTimer.IsRunning)
        {
            _quotaSaveTimer.Stop();
            AppSettingsStore.Save(Settings);
        }
    }

    private void CreateTrayIcon()
    {
        try
        {
            _tray = new TrayIcon(AppPaths.IconPath, Loc.Get("AppName"), BuildTrayMenu);
            _tray.Activated += (_, _) => ShowWindow(null);
        }
        catch (Exception ex)
        {
            LogTrayUnavailable(_logger, ex);
        }
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var hasMowers = Dashboard.Mowers.Count > 0;
        return
        [
            new(Loc.Get("Tray_Open"), () => ShowWindow(null)),
            new(Loc.Get("Tray_Refresh"), () => Dashboard.RefreshCommand.Execute(null), hasMowers),
            TrayMenuItem.Separator,
            new(Loc.Get("Tray_ParkAll"), () => Dashboard.ParkAllCommand.Execute(null), hasMowers),
            new(Loc.Get("Tray_ResumeAll"), () => Dashboard.ResumeAllCommand.Execute(null), hasMowers),
            TrayMenuItem.Separator,
            new(Loc.Get("Tray_Quit"), Quit),
        ];
    }

    private void UpdateTray()
    {
        if (_tray is null)
        {
            return;
        }
        var alert = Dashboard.ErrorCount > 0;
        _tray.Update(alert != _trayShowsAlert ? (alert ? AppPaths.AlertIconPath : AppPaths.IconPath) : null, Dashboard.TraySummary);
        _trayShowsAlert = alert;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled UI exception")]
    private static partial void LogUnhandledUiException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception")]
    private static partial void LogUnhandledException(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "HusqA Cockpit starting (culture {Culture})")]
    private static partial void LogStarting(ILogger logger, string culture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exiting")]
    private static partial void LogExiting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clean shutdown failed")]
    private static partial void LogShutdownFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not restart")]
    private static partial void LogRestartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not start monitoring")]
    private static partial void LogMonitoringStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alert {Kind} for {Mower} (code {Code})")]
    private static partial void LogAlert(ILogger logger, MowerAlertKind kind, string mower, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tray icon unavailable")]
    private static partial void LogTrayUnavailable(ILogger logger, Exception exception);
}
