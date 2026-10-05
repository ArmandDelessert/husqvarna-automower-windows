using HusqaCockpit.Core.Api;
using HusqaCockpit.Presentation.Tests.Support;
using HusqaCockpit.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace HusqaCockpit.Presentation.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly IDisposable _culture = TestCulture.Use(TestCulture.English);
    private readonly AppSettings _settings = new() { PollingIntervalMinutes = 10 };
    private readonly FakeSettingsStore _store = new();
    private readonly FakeCredentialStore _credentials = new();
    private readonly FakeCockpitHost _host = new();
    private readonly FakeShell _shell = new();
    private readonly FakeTimeProvider _time = new(TestMowers.Now.ToUniversalTime());

    public SettingsViewModelTests() => _time.SetLocalTimeZone(TestMowers.Zone);

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void Opening_the_page_changes_nothing()
    {
        _settings.Language = "fr-FR";
        _shell.StartsWithWindows = true;

        var page = Create();

        Assert.Equal(1, page.LanguageIndex);
        Assert.Equal(1, page.PollingIntervalIndex);
        Assert.True(page.StartWithWindows);
        Assert.False(page.IsRestartRequired);
        Assert.Equal(0, _store.Saves);
        Assert.Equal(0, _host.Restarts);
    }

    [Fact]
    public void Changing_the_polling_interval_saves_it_and_restarts_the_monitoring()
    {
        var page = Create();
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.PollingIntervalIndex = 3;

        Assert.Equal(30, _settings.PollingIntervalMinutes);
        Assert.Equal(1, _store.Saves);
        Assert.Equal(1, _host.Restarts);
        Assert.Contains(nameof(SettingsViewModel.PollingHelpText), changed);
        Assert.Equal(
            "Husqvarna allows 10,000 requests per month per key. Every 30 minutes uses about 1,440 requests per month.",
            page.PollingHelpText);
    }

    [Fact]
    public void Choosing_the_current_polling_interval_again_does_nothing()
    {
        var page = Create();

        page.PollingIntervalIndex = 1;

        Assert.Equal(0, _store.Saves);
        Assert.Equal(0, _host.Restarts);
    }

    [Fact]
    public void Polling_intervals_are_listed_in_minutes()
    {
        Assert.Equal(
            ["Every 5 minutes", "Every 10 minutes", "Every 15 minutes", "Every 30 minutes", "Every 60 minutes"],
            Create().PollingIntervalNames);
    }

    [Fact]
    public void Changing_the_language_saves_it_and_asks_for_a_restart()
    {
        var page = Create();

        page.LanguageIndex = 1;

        Assert.Equal("fr-FR", _settings.Language);
        Assert.Equal(1, _store.Saves);
        Assert.True(page.IsRestartRequired);
        Assert.Equal(0, _host.Restarts);
    }

    [Fact]
    public void Going_back_to_the_running_language_no_longer_asks_for_a_restart()
    {
        var page = Create();
        page.LanguageIndex = 2;

        page.LanguageIndex = 0;

        Assert.Equal("", _settings.Language);
        Assert.False(page.IsRestartRequired);
    }

    [Fact]
    public void Restart_goes_through_the_shell()
    {
        var page = Create();
        page.LanguageIndex = 2;

        page.RestartCommand.Execute(null);

        Assert.Equal(1, _shell.Restarts);
    }

    [Fact]
    public void Start_with_Windows_follows_the_switch()
    {
        var page = Create();

        page.StartWithWindows = true;
        Assert.True(_shell.StartsWithWindows);

        page.StartWithWindows = false;
        Assert.False(_shell.StartsWithWindows);
    }

    [Fact]
    public void Notification_switches_are_saved()
    {
        var page = Create();

        page.NotifyStopped = false;
        page.NotifyStopped = false;

        Assert.False(_settings.NotifyStopped);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public void Quota_counts_this_month_only()
    {
        _settings.QuotaMonth = "2026-06";
        _settings.QuotaRequests = 1234;
        Assert.Equal("API requests sent this month from this computer: 1,234 (Husqvarna limit: 10,000 per month).", Create().QuotaText);

        _settings.QuotaMonth = "2026-05";
        Assert.Equal("API requests sent this month from this computer: 0 (Husqvarna limit: 10,000 per month).", Create().QuotaText);
    }

    [Fact]
    public async Task Saving_the_key_stores_it_trimmed_and_reconnects()
    {
        var page = Create();
        Assert.False(page.SaveCredentialsCommand.CanExecute(null));

        page.ApplicationKey = " key ";
        page.ApplicationSecret = "secret\t";
        Assert.True(page.SaveCredentialsCommand.CanExecute(null));
        await page.SaveCredentialsCommand.ExecuteAsync(null);

        Assert.Equal(new ApiCredentials("key", "secret"), _credentials.Stored);
        Assert.True(page.HasStoredCredentials);
        Assert.Equal(1, _host.Restarts);
        Assert.True(page.IsCredentialResultVisible);
        Assert.Equal(StatusSeverity.Ok, page.CredentialResultSeverity);
        Assert.Equal("Key saved. Connecting…", page.CredentialResult);
    }

    [Fact]
    public async Task Testing_the_key_shows_the_result()
    {
        _credentials.Stored = new ApiCredentials("key", "secret");
        _host.CredentialTest = new CredentialTestResult(false, "Rejected.");
        var page = Create();

        await page.TestCredentialsCommand.ExecuteAsync(null);

        Assert.Equal(StatusSeverity.Error, page.CredentialResultSeverity);
        Assert.Equal("Rejected.", page.CredentialResult);
        // A test neither replaces nor removes the stored key.
        Assert.Equal(new ApiCredentials("key", "secret"), _credentials.Stored);
        Assert.Equal(0, _host.Restarts);
    }

    [Fact]
    public async Task Deleting_the_key_clears_the_fields_and_stops_the_monitoring()
    {
        _credentials.Stored = new ApiCredentials("key", "secret");
        var page = Create();
        Assert.Equal("key", page.ApplicationKey);

        await page.DeleteCredentialsCommand.ExecuteAsync(null);

        Assert.Null(_credentials.Stored);
        Assert.Equal("", page.ApplicationKey);
        Assert.Equal("", page.ApplicationSecret);
        Assert.False(page.HasStoredCredentials);
        Assert.Equal(1, _host.Restarts);
        Assert.Equal(StatusSeverity.Info, page.CredentialResultSeverity);
    }

    [Fact]
    public void Test_notification_goes_through_the_shell()
    {
        Create().SendTestNotificationCommand.Execute(null);

        Assert.Equal(["Test notification"], _shell.Notifications);
    }

    private SettingsViewModel Create() =>
        new(_settings, _store, _credentials, _host, _shell, ReswStrings.English, _time);
}
