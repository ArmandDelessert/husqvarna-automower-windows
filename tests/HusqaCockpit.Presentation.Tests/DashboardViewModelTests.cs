using System.ComponentModel;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;
using HusqaCockpit.Presentation.Tests.Support;
using HusqaCockpit.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace HusqaCockpit.Presentation.Tests;

public sealed class DashboardViewModelTests : IDisposable
{
    private readonly IDisposable _culture = TestCulture.Use(TestCulture.English);
    private readonly FakeTimeProvider _time = new(TestMowers.Now.ToUniversalTime());
    private readonly FakeCockpitHost _host = new();
    private readonly DashboardViewModel _dashboard;

    public DashboardViewModelTests()
    {
        _time.SetLocalTimeZone(TestMowers.Zone);
        _dashboard = new DashboardViewModel(_host, new InlineDispatcher(), ReswStrings.English, _time);
    }

    public void Dispose()
    {
        _dashboard.Dispose();
        _culture.Dispose();
    }

    // ----- Mowers -----

    [Fact]
    public void Mowers_are_sorted_by_name_ignoring_case()
    {
        Snapshot(("c", TestMowers.Parked("Charlie")), ("a", TestMowers.Parked("alpha")), ("b", TestMowers.Parked("Bravo")));

        Assert.Equal(["alpha", "Bravo", "Charlie"], _dashboard.Mowers.Select(m => m.Name));
        Assert.False(_dashboard.IsEmpty);
    }

    [Fact]
    public void A_new_mower_is_inserted_at_its_place_and_the_others_are_updated_in_place()
    {
        Snapshot(("a", TestMowers.Parked("Alpha")), ("c", TestMowers.Parked("Charlie")));
        var alpha = _dashboard.Mowers[0];

        Snapshot(("a", TestMowers.Mowing("Alpha")), ("c", TestMowers.Parked("Charlie")), ("b", TestMowers.Parked("Bravo")));

        Assert.Equal(["Alpha", "Bravo", "Charlie"], _dashboard.Mowers.Select(m => m.Name));
        Assert.Same(alpha, _dashboard.Mowers[0]);
        Assert.Equal("Mowing", alpha.StatusTitle);
    }

    [Fact]
    public void A_mower_gone_from_the_account_is_removed()
    {
        Snapshot(("a", TestMowers.Parked("Alpha")), ("b", TestMowers.Parked("Bravo")));

        Snapshot(("b", TestMowers.Parked("Bravo")));
        Assert.Equal(["Bravo"], _dashboard.Mowers.Select(m => m.Name));
        Assert.Null(_dashboard.Find("a"));

        Snapshot();
        Assert.Empty(_dashboard.Mowers);
        Assert.True(_dashboard.IsEmpty);
    }

    [Fact]
    public void Summary_changes_are_announced()
    {
        var raised = 0;
        _dashboard.SummaryChanged += (_, _) => raised++;

        Snapshot(("a", TestMowers.Parked("Alpha")), ("b", TestMowers.Parked("Bravo")));
        Snapshot(("a", TestMowers.Parked("Alpha")));

        // Two mowers appear; then one disappears and the other is refreshed.
        Assert.Equal(4, raised);
    }

    [Fact]
    public void Error_count_and_tray_summary()
    {
        Assert.Equal("HusqA Cockpit", _dashboard.TraySummary);

        Snapshot(("a", TestMowers.Mowing("Alpha")), ("b", TestMowers.Parked("Bravo")), ("c", TestMowers.Parked("Charlie")));
        Assert.Equal(0, _dashboard.ErrorCount);
        Assert.Equal("HusqA Cockpit\n3 mower(s) · 1 mowing", _dashboard.TraySummary);

        Snapshot(("a", TestMowers.Mowing("Alpha")), ("b", TestMowers.InError(1, "Bravo")), ("c", TestMowers.InError(69, "Charlie")));
        Assert.Equal(2, _dashboard.ErrorCount);
        Assert.Equal("HusqA Cockpit\n3 mower(s) · 1 mowing\n2 with a problem", _dashboard.TraySummary);
    }

    [Fact]
    public void Relative_texts_are_refreshed_every_30_seconds()
    {
        Snapshot(("a", TestMowers.Parked("Alpha")));
        var mower = _dashboard.Mowers[0];
        var changed = new List<string?>();
        mower.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("Updated 5 min ago", mower.LastSeenText);

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(changed);

        _time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.Contains(nameof(MowerViewModel.LastSeenText), changed);
        Assert.Contains(nameof(MowerViewModel.StatusDetail), changed);
        Assert.Equal("Updated 6 min ago", mower.LastSeenText);
    }

    // ----- Connection banner -----

    [Fact]
    public void Missing_key_asks_for_the_settings()
    {
        _host.HasCredentials = false;

        _host.SetStatus(new MonitorStatus(MonitorState.Stopped));

        AssertBanner(StatusSeverity.Warning, "Welcome!", "Enter your Husqvarna API key to see your mowers.", needsSettings: true);
        Assert.Equal("API key required", _dashboard.ConnectionLabel);
    }

    [Fact]
    public void Stopped_with_a_key_shows_no_banner()
    {
        _host.SetStatus(new MonitorStatus(MonitorState.Stopped));

        Assert.False(_dashboard.IsStatusVisible);
        Assert.Equal("Offline", _dashboard.ConnectionLabel);
    }

    [Fact]
    public void Starting()
    {
        _host.SetStatus(new MonitorStatus(MonitorState.Starting));

        AssertBanner(StatusSeverity.Info, "Connecting to Husqvarna…", "");
        Assert.Equal("Connecting…", _dashboard.ConnectionLabel);
    }

    [Fact]
    public void Live_hides_the_banner()
    {
        _host.SetStatus(new MonitorStatus(MonitorState.Starting));

        _host.SetStatus(new MonitorStatus(MonitorState.Live, EventStreamState.Connected, TestMowers.Now));

        Assert.False(_dashboard.IsStatusVisible);
        Assert.Equal("Real time", _dashboard.ConnectionLabel);
    }

    [Fact]
    public void Polling_tells_the_last_and_next_refresh()
    {
        _host.SetStatus(new MonitorStatus(
            MonitorState.Polling, EventStreamState.Disconnected, TestMowers.Now, TestMowers.Now.AddMinutes(10)));

        AssertBanner(
            StatusSeverity.Info,
            "Real-time updates unavailable",
            "The mowers are refreshed periodically. Last update 12:00, next 12:10.");
        Assert.Equal("Periodic refresh", _dashboard.ConnectionLabel);
    }

    [Fact]
    public void Polling_because_the_event_service_refused_warns_about_the_application_setup()
    {
        _host.SetStatus(new MonitorStatus(MonitorState.Polling, EventStreamState.Forbidden));

        Assert.True(_dashboard.IsStatusVisible);
        Assert.Equal(StatusSeverity.Warning, _dashboard.StatusSeverity);
        Assert.StartsWith("Husqvarna refused the real-time connection", _dashboard.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejected_key_asks_for_the_settings()
    {
        _host.SetStatus(new MonitorStatus(MonitorState.AuthenticationFailed, Detail: "invalid_client"));

        AssertBanner(
            StatusSeverity.Error,
            "API key rejected",
            "Husqvarna rejected the application key or secret. Check them in the settings.",
            needsSettings: true);
    }

    [Fact]
    public void Offline_tells_the_error_and_the_next_attempt()
    {
        _host.SetStatus(new MonitorStatus(MonitorState.Offline, NextRefresh: TestMowers.Now.AddMinutes(1), Detail: "No such host is known."));

        AssertBanner(StatusSeverity.Error, "Husqvarna servers unreachable", "No such host is known. Next attempt at 12:01.");
        Assert.Equal("Offline", _dashboard.ConnectionLabel);
    }

    // ----- Commands -----

    [Fact]
    public async Task Park_all_parks_every_mower_until_further_notice()
    {
        Snapshot(("a", TestMowers.Mowing("Alpha")), ("b", TestMowers.Mowing("Bravo")));

        await _dashboard.ParkAllCommand.ExecuteAsync(null);

        Assert.Equal(["ParkUntilFurtherNotice a", "ParkUntilFurtherNotice b"], _host.Commands);
        Assert.Equal("All mowers are going back to their station until further notice.", _dashboard.CommandMessage);
        Assert.False(_dashboard.IsBusy);
    }

    [Fact]
    public async Task Resume_all_names_the_mowers_it_failed_for()
    {
        Snapshot(("a", TestMowers.Parked("Alpha")), ("b", TestMowers.Parked("Bravo")));
        _host.Failing.Add("a");

        await _dashboard.ResumeAllCommand.ExecuteAsync(null);

        Assert.Equal(["ResumeSchedule b"], _host.Commands);
        Assert.Equal("The command failed for: Alpha (mower offline)", _dashboard.CommandMessage);
    }

    [Fact]
    public async Task Failed_refresh_is_reported()
    {
        _host.RefreshError = new HttpRequestException("timeout");

        await _dashboard.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Command failed: timeout", _dashboard.CommandMessage);
        Assert.True(_dashboard.HasCommandMessage);
    }

    private void Snapshot(params (string Id, MowerAttributes Attributes)[] mowers) =>
        _host.Fleet.ApplySnapshot(mowers.Select(m => TestMowers.Resource(m.Id, m.Attributes)));

    private void AssertBanner(StatusSeverity severity, string title, string message, bool needsSettings = false)
    {
        Assert.True(_dashboard.IsStatusVisible);
        Assert.Equal(severity, _dashboard.StatusSeverity);
        Assert.Equal(title, _dashboard.StatusTitle);
        Assert.Equal(message, _dashboard.StatusMessage);
        Assert.Equal(needsSettings, _dashboard.StatusNeedsSettings);
    }
}
