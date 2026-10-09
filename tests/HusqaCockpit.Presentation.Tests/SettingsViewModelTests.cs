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
        Assert.Equal("API requests sent this month from this computer: 1,234 (the API is limited to 10,000 requests per month).", Create().QuotaText);

        _settings.QuotaMonth = "2026-05";
        Assert.Equal("API requests sent this month from this computer: 0 (the API is limited to 10,000 requests per month).", Create().QuotaText);
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

    // ----- Notifications about the mowing tasks -----

    [Fact]
    public void Task_notifications_default_to_none_and_the_choices_are_named()
    {
        var page = Create();

        Assert.Equal(0, page.TaskNotificationsIndex);
        Assert.Equal(TaskNotificationMode.None, _settings.TaskNotifications);
        Assert.Equal(["None", "Mowing end only", "Mowing start and end"], page.TaskNotificationNames);
    }

    [Fact]
    public void The_task_choices_are_named_in_French()
    {
        using var french = TestCulture.Use(TestCulture.French);

        var page = new SettingsViewModel(_settings, _store, _credentials, _host, _shell, ReswStrings.French, _time);

        Assert.Equal(["Aucune", "Fin de tonte seulement", "Début et fin de tonte"], page.TaskNotificationNames);
    }

    [Fact]
    public void Opening_the_page_shows_the_saved_task_choice_without_saving()
    {
        _settings.TaskNotifications = TaskNotificationMode.StartAndEnd;

        var page = Create();

        Assert.Equal(2, page.TaskNotificationsIndex);
        Assert.Equal(0, _store.Saves);
    }

    [Theory]
    [InlineData(1, TaskNotificationMode.EndOnly)]
    [InlineData(2, TaskNotificationMode.StartAndEnd)]
    public void Choosing_a_task_notification_saves_it(int index, TaskNotificationMode expected)
    {
        var page = Create();

        page.TaskNotificationsIndex = index;

        Assert.Equal(expected, _settings.TaskNotifications);
        Assert.Equal(1, _store.Saves);
        Assert.Equal(0, _host.Restarts);
    }

    [Fact]
    public void Choosing_the_current_task_notification_again_saves_nothing()
    {
        var page = Create();

        page.TaskNotificationsIndex = 0;

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void Clearing_the_task_choice_in_the_list_changes_nothing()
    {
        // A ComboBox reports -1 while its items are replaced.
        var page = Create();
        page.TaskNotificationsIndex = 2;

        page.TaskNotificationsIndex = -1;

        Assert.Equal(TaskNotificationMode.StartAndEnd, _settings.TaskNotifications);
        Assert.Equal(1, _store.Saves);
    }

    // ----- The version line -----

    [Theory]
    [InlineData("0.1.0+a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "0.1.0")]
    [InlineData("0.1.0-beta.1+a1b2c3d", "0.1.0-beta.1")]
    [InlineData("0.0.0-dev", "0.0.0-dev")]
    [InlineData("1.2.3+", "1.2.3")]
    [InlineData("", "?")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void The_version_is_what_the_build_carries_without_the_commit(string? informational, string expected) =>
        Assert.Equal(expected, SettingsViewModel.VersionOf(informational));

    [Theory]
    [InlineData("0.1.0+a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "a1b2c3d")]
    [InlineData("0.1.0+abc", "abc")]
    [InlineData("0.1.0+ a1b2c3d4 ", "a1b2c3d")]
    [InlineData("0.1.0+", null)]
    [InlineData("0.1.0+  ", null)]
    [InlineData("0.1.0", null)]
    [InlineData(null, null)]
    public void The_commit_is_the_first_seven_characters_after_the_plus(string? informational, string? expected) =>
        Assert.Equal(expected, SettingsViewModel.CommitOf(informational));

    [Theory]
    [InlineData("0.1.0+a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "HusqA Cockpit 0.1.0 (a1b2c3d)")]
    [InlineData("0.1.0-beta.1+a1b2c3d", "HusqA Cockpit 0.1.0-beta.1 (a1b2c3d)")]
    [InlineData("0.1.0", "HusqA Cockpit 0.1.0")]
    [InlineData(null, "HusqA Cockpit ?")]
    public void The_version_line_gives_the_version_then_its_commit_when_there_is_one(string? informational, string expected) =>
        Assert.Equal(expected, SettingsViewModel.VersionTextOf(ReswStrings.English, informational));

    [Fact]
    public void The_page_shows_the_version_of_the_running_build()
    {
        // The test assembly is built by the same SDK, which puts the commit after the "+" of its version.
        var page = Create();

        Assert.StartsWith("HusqA Cockpit ", page.VersionText, StringComparison.Ordinal);
        Assert.DoesNotContain("+", page.VersionText, StringComparison.Ordinal);
    }

    private SettingsViewModel Create() =>
        new(_settings, _store, _credentials, _host, _shell, ReswStrings.English, _time);
}
