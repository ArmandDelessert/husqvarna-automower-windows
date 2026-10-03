using System.Globalization;
using HusqaCockpit.App.Services;
using HusqaCockpit.App.ViewModels;
using HusqaCockpit.Core.Fleet;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace HusqaCockpit.App;

public partial class App : Application
{
    private readonly bool _startMinimized;
    private ILoggerFactory _loggers = null!;
    private ILogger _logger = null!;
    private DispatcherQueue _dispatcher = null!;
    private TrayIcon? _tray;
    private bool _trayShowsAlert;

    public App(bool startMinimized)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        UnhandledException += (_, e) => _logger?.LogError(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.LogWarning(e.Exception, "Unobserved task exception");
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
        _logger.LogInformation("HusqA Cockpit starting (culture {Culture})", CultureInfo.CurrentUICulture.Name);

        Settings = AppSettingsStore.Load();
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
        _logger.LogInformation("Exiting");
        _tray?.Dispose();
        _tray = null;
        Window.AppWindow.Hide();
        try
        {
            await Host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Clean shutdown failed");
        }
        _loggers.Dispose();
        Exit();
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
            _logger.LogError(ex, "Could not start monitoring");
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
            _logger.LogInformation("Alert {Kind} for {Mower} (code {Code})", alert.Kind, alert.Mower.Name, alert.ErrorCode);
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
        AppSettingsStore.Save(Settings);
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
            _logger.LogWarning(ex, "Tray icon unavailable");
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
}
