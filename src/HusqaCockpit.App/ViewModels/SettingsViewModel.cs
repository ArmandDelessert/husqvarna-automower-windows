using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.App.Services;
using HusqaCockpit.Core.Api;
using Microsoft.UI.Xaml.Controls;

namespace HusqaCockpit.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public const int MonthlyQuota = 10_000;
    private static readonly string[] s_languages = ["", "fr-FR", "en-US"];
    private static readonly int[] s_pollingIntervals = [5, 10, 15, 30, 60];

    private readonly AppSettings _settings;
    private readonly CredentialStore _credentials;
    private readonly CockpitHost _host;
    private readonly string _initialLanguage;
    private readonly bool _initialized;

    public SettingsViewModel(AppSettings settings, CredentialStore credentials, CockpitHost host)
    {
        _settings = settings;
        _credentials = credentials;
        _host = host;
        _initialLanguage = settings.Language;

        var stored = credentials.LoadCredentials();
        ApplicationKey = stored?.ApplicationKey ?? "";
        ApplicationSecret = stored?.ApplicationSecret ?? "";
        HasStoredCredentials = stored is not null;
        LanguageIndex = Math.Max(0, Array.IndexOf(s_languages, settings.Language));
        PollingIntervalIndex = Math.Max(0, Array.IndexOf(s_pollingIntervals, settings.PollingIntervalMinutes));
        StartWithWindows = StartupRegistration.IsEnabled;
        _initialized = true;
    }

    // ----- API credentials -----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCredentialsCommand), nameof(SaveCredentialsCommand))]
    public partial string ApplicationKey { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCredentialsCommand), nameof(SaveCredentialsCommand))]
    public partial string ApplicationSecret { get; set; }

    [ObservableProperty]
    public partial bool HasStoredCredentials { get; set; }

    [ObservableProperty]
    public partial bool IsCredentialResultVisible { get; set; }

    [ObservableProperty]
    public partial string CredentialResult { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity CredentialResultSeverity { get; set; }

    private bool CanUseCredentials() =>
        !string.IsNullOrWhiteSpace(ApplicationKey) && !string.IsNullOrWhiteSpace(ApplicationSecret);

    private ApiCredentials Credentials => new(ApplicationKey.Trim(), ApplicationSecret.Trim());

    [RelayCommand(CanExecute = nameof(CanUseCredentials))]
    private async Task TestCredentialsAsync()
    {
        var result = await _host.TestCredentialsAsync(Credentials);
        ShowResult(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
    }

    [RelayCommand(CanExecute = nameof(CanUseCredentials))]
    private async Task SaveCredentialsAsync()
    {
        _credentials.SaveCredentials(Credentials);
        HasStoredCredentials = true;
        ShowResult(InfoBarSeverity.Success, Loc.Get("Settings_Saved"));
        await _host.RestartAsync();
    }

    [RelayCommand]
    private async Task DeleteCredentialsAsync()
    {
        _credentials.DeleteCredentials();
        ApplicationKey = "";
        ApplicationSecret = "";
        HasStoredCredentials = false;
        ShowResult(InfoBarSeverity.Informational, Loc.Get("Settings_Deleted"));
        await _host.RestartAsync();
    }

    private void ShowResult(InfoBarSeverity severity, string message)
    {
        CredentialResultSeverity = severity;
        CredentialResult = message;
        IsCredentialResultVisible = true;
    }

    // ----- Notifications -----

    public bool NotifyErrors
    {
        get => _settings.NotifyErrors;
        set => Update(_settings.NotifyErrors, value, v => _settings.NotifyErrors = v);
    }

    public bool NotifyRecoveries
    {
        get => _settings.NotifyRecoveries;
        set => Update(_settings.NotifyRecoveries, value, v => _settings.NotifyRecoveries = v);
    }

    public bool NotifyStopped
    {
        get => _settings.NotifyStopped;
        set => Update(_settings.NotifyStopped, value, v => _settings.NotifyStopped = v);
    }

    public bool NotifyConnectivity
    {
        get => _settings.NotifyConnectivity;
        set => Update(_settings.NotifyConnectivity, value, v => _settings.NotifyConnectivity = v);
    }

    [RelayCommand]
    private static void SendTestNotification() =>
        App.Current.Notifications.ShowInfo(Loc.Get("Notification_TestTitle"), Loc.Get("Notification_TestBody"));

    // ----- Behavior -----

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set => Update(_settings.CloseToTray, value, v => _settings.CloseToTray = v);
    }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_initialized && value != StartupRegistration.IsEnabled)
        {
            StartupRegistration.SetEnabled(value);
        }
    }

    [ObservableProperty]
    public partial int LanguageIndex { get; set; }

    [ObservableProperty]
    public partial bool IsRestartRequired { get; set; }

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_initialized || value < 0 || value >= s_languages.Length)
        {
            return;
        }
        _settings.Language = s_languages[value];
        AppSettingsStore.Save(_settings);
        IsRestartRequired = _settings.Language != _initialLanguage;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollingHelpText))]
    public partial int PollingIntervalIndex { get; set; }

    /// <summary>Explains the monthly request cost of the selected interval (one request per refresh, 30-day month).</summary>
    public string PollingHelpText
    {
        get
        {
            var minutes = s_pollingIntervals[Math.Clamp(PollingIntervalIndex, 0, s_pollingIntervals.Length - 1)];
            var perMonth = 30 * 24 * 60 / minutes;
            return Loc.Format("Settings_PollingHelp",
                MonthlyQuota.ToString("N0", CultureInfo.CurrentCulture),
                minutes,
                perMonth.ToString("N0", CultureInfo.CurrentCulture));
        }
    }

    public IReadOnlyList<string> PollingIntervalNames { get; } =
        s_pollingIntervals.Select(m => Loc.Format("Settings_EveryMinutes", m)).ToList();

    partial void OnPollingIntervalIndexChanged(int value)
    {
        if (!_initialized || value < 0 || value >= s_pollingIntervals.Length || s_pollingIntervals[value] == _settings.PollingIntervalMinutes)
        {
            return;
        }
        _settings.PollingIntervalMinutes = s_pollingIntervals[value];
        AppSettingsStore.Save(_settings);
        _ = _host.RestartAsync();
    }

    // ----- Usage & about -----

    public string QuotaText => _settings.QuotaMonth == DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        ? Loc.Format("Settings_Quota", _settings.QuotaRequests.ToString("N0", CultureInfo.CurrentCulture), MonthlyQuota.ToString("N0", CultureInfo.CurrentCulture))
        : Loc.Format("Settings_Quota", 0, MonthlyQuota.ToString("N0", CultureInfo.CurrentCulture));

    public string VersionText => Loc.Format("Settings_Version",
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?");

    public void RefreshQuota() => OnPropertyChanged(nameof(QuotaText));

    [RelayCommand]
    private static void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);
        Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
    }

    private void Update(bool current, bool value, Action<bool> apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (current == value)
        {
            return;
        }
        apply(value);
        AppSettingsStore.Save(_settings);
        OnPropertyChanged(property);
    }
}
