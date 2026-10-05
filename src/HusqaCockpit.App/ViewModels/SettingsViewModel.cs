using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Presentation;

namespace HusqaCockpit.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public const int MonthlyQuota = 10_000;
    private static readonly string[] s_languages = ["", "fr-FR", "en-US"];
    private static readonly int[] s_pollingIntervals = [5, 10, 15, 30, 60];

    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly ICredentialStore _credentials;
    private readonly ICockpitHost _host;
    private readonly IAppShell _shell;
    private readonly IStrings _strings;
    private readonly TimeProvider _time;
    private readonly string _initialLanguage;
    private readonly bool _initialized;

    public SettingsViewModel(
        AppSettings settings,
        ISettingsStore store,
        ICredentialStore credentials,
        ICockpitHost host,
        IAppShell shell,
        IStrings strings,
        TimeProvider time)
    {
        _settings = settings;
        _store = store;
        _credentials = credentials;
        _host = host;
        _shell = shell;
        _strings = strings;
        _time = time;
        _initialLanguage = settings.Language;
        PollingIntervalNames = s_pollingIntervals.Select(m => strings.Format("Settings_EveryMinutes", m)).ToList();
        VersionText = strings.Format("Settings_Version",
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?");

        var stored = credentials.LoadCredentials();
        ApplicationKey = stored?.ApplicationKey ?? "";
        ApplicationSecret = stored?.ApplicationSecret ?? "";
        HasStoredCredentials = stored is not null;
        LanguageIndex = Math.Max(0, Array.IndexOf(s_languages, settings.Language));
        PollingIntervalIndex = Math.Max(0, Array.IndexOf(s_pollingIntervals, settings.PollingIntervalMinutes));
        StartWithWindows = shell.StartsWithWindows;
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
    public partial StatusSeverity CredentialResultSeverity { get; set; }

    private bool CanUseCredentials() =>
        !string.IsNullOrWhiteSpace(ApplicationKey) && !string.IsNullOrWhiteSpace(ApplicationSecret);

    private ApiCredentials Credentials => new(ApplicationKey.Trim(), ApplicationSecret.Trim());

    [RelayCommand(CanExecute = nameof(CanUseCredentials))]
    private async Task TestCredentialsAsync()
    {
        var result = await _host.TestCredentialsAsync(Credentials);
        ShowResult(result.Success ? StatusSeverity.Ok : StatusSeverity.Error, result.Message);
    }

    [RelayCommand(CanExecute = nameof(CanUseCredentials))]
    private async Task SaveCredentialsAsync()
    {
        _credentials.SaveCredentials(Credentials);
        HasStoredCredentials = true;
        ShowResult(StatusSeverity.Ok, _strings.Text("Settings_Saved"));
        await _host.RestartAsync();
    }

    [RelayCommand]
    private async Task DeleteCredentialsAsync()
    {
        _credentials.DeleteCredentials();
        ApplicationKey = "";
        ApplicationSecret = "";
        HasStoredCredentials = false;
        ShowResult(StatusSeverity.Info, _strings.Text("Settings_Deleted"));
        await _host.RestartAsync();
    }

    private void ShowResult(StatusSeverity severity, string message)
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
    private void SendTestNotification() =>
        _shell.ShowNotification(_strings.Text("Notification_TestTitle"), _strings.Text("Notification_TestBody"));

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
        if (_initialized && value != _shell.StartsWithWindows)
        {
            _shell.StartsWithWindows = value;
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
        _store.Save(_settings);
        IsRestartRequired = _settings.Language != _initialLanguage;
    }

    [RelayCommand]
    private void Restart() => _shell.Restart();

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
            return _strings.Format("Settings_PollingHelp",
                MonthlyQuota.ToString("N0", CultureInfo.CurrentCulture),
                minutes,
                perMonth.ToString("N0", CultureInfo.CurrentCulture));
        }
    }

    public IReadOnlyList<string> PollingIntervalNames { get; }

    partial void OnPollingIntervalIndexChanged(int value)
    {
        if (!_initialized || value < 0 || value >= s_pollingIntervals.Length || s_pollingIntervals[value] == _settings.PollingIntervalMinutes)
        {
            return;
        }
        _settings.PollingIntervalMinutes = s_pollingIntervals[value];
        _store.Save(_settings);
        _ = _host.RestartAsync();
    }

    // ----- Usage & about -----

    public string QuotaText => _settings.QuotaMonth == _time.GetLocalNow().ToString("yyyy-MM", CultureInfo.InvariantCulture)
        ? _strings.Format("Settings_Quota", _settings.QuotaRequests.ToString("N0", CultureInfo.CurrentCulture), MonthlyQuota.ToString("N0", CultureInfo.CurrentCulture))
        : _strings.Format("Settings_Quota", 0, MonthlyQuota.ToString("N0", CultureInfo.CurrentCulture));

    public string VersionText { get; }

    public void RefreshQuota() => OnPropertyChanged(nameof(QuotaText));

    [RelayCommand]
    private void OpenLogsFolder() => _shell.OpenLogsFolder();

    private void Update(bool current, bool value, Action<bool> apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (current == value)
        {
            return;
        }
        apply(value);
        _store.Save(_settings);
        OnPropertyChanged(property);
    }
}
