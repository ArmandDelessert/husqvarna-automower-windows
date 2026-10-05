using System.Net;
using System.Text.Json.Nodes;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;

namespace HusqaCockpit.Core.Tests;

/// <summary>GET /mowers, with failures and holds chosen by the test.</summary>
internal sealed class FakeSnapshots(TimeProvider clock) : IMowerSnapshotSource
{
    private readonly Lock _lock = new();
    private readonly Queue<Exception> _failures = new();
    private readonly List<DateTimeOffset> _calls = [];
    private TaskCompletionSource? _hold;

    /// <summary>When each request was made.</summary>
    public IReadOnlyList<DateTimeOffset> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public int Count => Calls.Count;

    /// <summary>The next requests fail with these exceptions, in order; the following ones succeed.</summary>
    public void FailNext(params Exception[] failures)
    {
        lock (_lock)
        {
            foreach (var failure in failures)
            {
                _failures.Enqueue(failure);
            }
        }
    }

    /// <summary>Requests do not complete until <see cref="Release"/>.</summary>
    public void Hold() => _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _hold?.TrySetResult();

    public async Task<IReadOnlyList<JsonObject>> GetMowerResourcesAsync(CancellationToken cancellationToken = default)
    {
        Exception? failure;
        lock (_lock)
        {
            _calls.Add(clock.GetUtcNow());
            failure = _failures.Count > 0 ? _failures.Dequeue() : null;
        }

        if (_hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }
        return failure is null ? TestData.MowerResources() : throw failure;
    }
}

/// <summary>An event feed whose statuses and events are pushed by the test.</summary>
internal sealed class FakeEventFeed : IAutomowerEventFeed
{
    private volatile Func<AutomowerEvent, ValueTask>? _onEvent;
    private volatile Action<EventStreamStatus>? _onStatus;

    public bool Stopped { get; private set; }

    public async Task RunAsync(Func<AutomowerEvent, ValueTask> onEvent, Action<EventStreamStatus> onStatus, CancellationToken cancellationToken)
    {
        _onEvent = onEvent;
        _onStatus = onStatus;
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Stopped = true;
        }
    }

    public async Task ReportAsync(EventStreamStatus status)
    {
        await TestClock.Until(() => _onStatus is not null, "the feed runs");
        _onStatus!(status);
    }

    public async Task SendAsync(AutomowerEvent evt)
    {
        await TestClock.Until(() => _onEvent is not null, "the feed runs");
        await _onEvent!(evt);
    }
}

public sealed class FleetMonitorTests : IAsyncDisposable
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_live = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan s_polling = TimeSpan.FromMinutes(10);

    private readonly TestClock _clock = new(s_t0);
    private readonly FakeSnapshots _snapshots;
    private readonly FakeEventFeed _feed = new();
    private readonly MowerFleet _fleet = new(TestData.Zurich);
    private readonly FleetMonitor _monitor;

    public FleetMonitorTests()
    {
        _snapshots = new FakeSnapshots(_clock);
        _monitor = new FleetMonitor(_snapshots, _feed, _fleet, new FleetMonitorOptions(), _clock);
    }

    public ValueTask DisposeAsync() => _monitor.DisposeAsync();

    // ----- States -----

    [Fact]
    public async Task Starting_until_the_first_refresh_then_Polling_without_events()
    {
        _snapshots.Hold();
        _monitor.Start();
        await _feed.ReportAsync(new EventStreamStatus(EventStreamState.Connecting));
        await TestClock.Until(() => _snapshots.Count == 1, "the first refresh starts");
        Assert.Equal(MonitorState.Starting, _monitor.Status.State);

        _snapshots.Release();

        var status = await StatusAsync(s => s.NextRefresh is not null);
        Assert.Equal(MonitorState.Polling, status.State);
        Assert.Equal(EventStreamState.Connecting, status.StreamState);
        Assert.Equal(s_t0, status.LastRefresh);
        Assert.Equal(s_t0 + s_polling, status.NextRefresh);
        Assert.Equal(2, _fleet.Mowers.Count);
    }

    [Fact]
    public async Task Live_while_events_flow_with_a_safety_refresh_every_15_minutes()
    {
        _snapshots.Hold();
        _monitor.Start();
        await _feed.ReportAsync(new EventStreamStatus(EventStreamState.Connected));
        _snapshots.Release();

        var status = await StatusAsync(s => s.NextRefresh is not null);
        Assert.Equal(MonitorState.Live, status.State);
        Assert.Equal(s_t0 + s_live, status.NextRefresh);

        await _clock.AdvanceWhenWaitingAsync(s_live);

        await TestClock.Until(() => _snapshots.Count == 2, "the second refresh");
        Assert.Equal([s_t0, s_t0 + s_live], _snapshots.Calls);
    }

    [Fact]
    public async Task Back_to_Polling_and_refreshed_at_once_when_events_stop()
    {
        await StartLiveAsync();
        await _clock.WaitForTimerAsync(s_live);
        _clock.Advance(TimeSpan.FromMinutes(2));

        await _feed.ReportAsync(new EventStreamStatus(EventStreamState.Disconnected, RetryAt: s_t0 + TimeSpan.FromMinutes(3)));

        await TestClock.Until(() => _snapshots.Count == 2, "the refresh after the disconnection");
        var status = await StatusAsync(s => s.NextRefresh == s_t0 + TimeSpan.FromMinutes(2) + s_polling);
        Assert.Equal(MonitorState.Polling, status.State);
        Assert.Equal(EventStreamState.Disconnected, status.StreamState);
    }

    [Fact]
    public async Task Offline_when_a_refresh_fails_without_events()
    {
        _snapshots.FailNext(new HttpRequestException("No such host is known."));

        _monitor.Start();

        var status = await StatusAsync(s => s.NextRefresh is not null);
        Assert.Equal(MonitorState.Offline, status.State);
        Assert.Equal("No such host is known.", status.Detail);
        Assert.Null(status.LastRefresh);
        Assert.Equal(s_t0 + TimeSpan.FromSeconds(15), status.NextRefresh);
    }

    [Fact]
    public async Task Events_keep_the_state_Live_when_a_refresh_fails()
    {
        _snapshots.FailNext(new HttpRequestException("timeout"));
        _snapshots.Hold();
        _monitor.Start();
        await _feed.ReportAsync(new EventStreamStatus(EventStreamState.Connected));

        _snapshots.Release();

        var status = await StatusAsync(s => s.NextRefresh is not null);
        Assert.Equal(MonitorState.Live, status.State);
        Assert.Equal(s_t0 + TimeSpan.FromSeconds(15), status.NextRefresh);
    }

    [Fact]
    public async Task AuthenticationFailed_when_the_key_is_rejected_then_retried_after_30_minutes()
    {
        _snapshots.FailNext(new AuthenticationException(HttpStatusCode.Unauthorized, "invalid_client", null, "Client authentication failed."));

        _monitor.Start();

        var status = await StatusAsync(s => s.NextRefresh is not null);
        Assert.Equal(MonitorState.AuthenticationFailed, status.State);
        Assert.Equal("Client authentication failed.", status.Detail);
        Assert.Equal(s_t0 + TimeSpan.FromMinutes(30), status.NextRefresh);

        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromMinutes(30));

        status = await StatusAsync(s => s.State == MonitorState.Polling);
        Assert.Equal(s_t0 + TimeSpan.FromMinutes(30), status.LastRefresh);
    }

    [Fact]
    public async Task Too_many_logins_waits_one_minute()
    {
        _snapshots.FailNext(new AuthenticationException(HttpStatusCode.BadRequest, "invalid_request", "simultaneous.logins", "Too many logins."));

        _monitor.Start();

        var status = await StatusAsync(s => s.NextRefresh is not null);
        Assert.Equal(MonitorState.Offline, status.State);
        Assert.Equal(s_t0 + TimeSpan.FromMinutes(1), status.NextRefresh);
    }

    // ----- Refresh timing -----

    [Fact]
    public async Task Failures_back_off_then_the_normal_interval_resumes()
    {
        TimeSpan[] delays =
        [
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
        ];
        _snapshots.FailNext([.. delays.Select(_ => new HttpRequestException("unreachable"))]);

        _monitor.Start();

        var now = s_t0;
        foreach (var delay in delays)
        {
            var next = now + delay;
            await StatusAsync(s => s.NextRefresh == next);
            await _clock.AdvanceWhenWaitingAsync(delay);
            now = next;
        }
        var status = await StatusAsync(s => s.NextRefresh == now + s_polling);
        Assert.Equal(MonitorState.Polling, status.State);
        Assert.Equal(delays.Length + 1, _snapshots.Count);
    }

    [Fact]
    public async Task Requested_refreshes_are_spaced_by_30_seconds()
    {
        _monitor.Start();
        await _clock.WaitForTimerAsync(s_polling);
        _clock.Advance(TimeSpan.FromSeconds(5));

        _monitor.RequestRefresh();
        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromSeconds(25));

        await TestClock.Until(() => _snapshots.Count == 2, "the requested refresh");
        Assert.Equal([s_t0, s_t0 + TimeSpan.FromSeconds(30)], _snapshots.Calls);
    }

    [Fact]
    public async Task A_refresh_requested_long_after_the_last_one_runs_at_once()
    {
        _monitor.Start();
        await _clock.WaitForTimerAsync(s_polling);
        _clock.Advance(TimeSpan.FromMinutes(5));

        _monitor.RequestRefresh();

        await TestClock.Until(() => _snapshots.Count == 2, "the requested refresh");
        Assert.Equal(s_t0 + TimeSpan.FromMinutes(5), _snapshots.Calls[1]);
    }

    [Fact]
    public async Task Refreshes_when_events_come_back_after_an_outage()
    {
        _monitor.Start();
        await _clock.WaitForTimerAsync(s_polling);
        _clock.Advance(TimeSpan.FromMinutes(1));

        await _feed.ReportAsync(new EventStreamStatus(EventStreamState.Connected, AfterOutage: true));

        await TestClock.Until(() => _snapshots.Count == 2, "the refresh after the outage");
        var status = await StatusAsync(s => s.NextRefresh == s_t0 + TimeSpan.FromMinutes(1) + s_live);
        Assert.Equal(MonitorState.Live, status.State);
    }

    // ----- Events -----

    [Fact]
    public async Task Events_update_known_mowers_without_a_refresh()
    {
        _monitor.Start();
        await _clock.WaitForTimerAsync(s_polling);

        await _feed.SendAsync(new AutomowerEvent(
            "battery-event-v2", TestData.FrontLawnId, new JsonObject { ["battery"] = new JsonObject { ["batteryPercent"] = 42 } }));

        Assert.Equal(42, _fleet.Find(TestData.FrontLawnId)!.BatteryPercent);
        Assert.Equal(1, _snapshots.Count);
    }

    [Fact]
    public async Task Event_for_an_unknown_mower_triggers_a_refresh()
    {
        _monitor.Start();
        await _clock.WaitForTimerAsync(s_polling);
        _clock.Advance(TimeSpan.FromMinutes(1));

        await _feed.SendAsync(new AutomowerEvent("mower-event-v2", "11111111-aaaa-4bbb-8ccc-000000000099", []));

        await TestClock.Until(() => _snapshots.Count == 2, "the refresh for the new mower");
    }

    [Fact]
    public async Task Stopping_ends_the_feed_and_reports_Stopped()
    {
        _monitor.Start();
        await _clock.WaitForTimerAsync(s_polling);

        await _monitor.StopAsync();

        Assert.Equal(MonitorState.Stopped, _monitor.Status.State);
        Assert.True(_feed.Stopped);
    }

    private async Task StartLiveAsync()
    {
        _snapshots.Hold();
        _monitor.Start();
        await _feed.ReportAsync(new EventStreamStatus(EventStreamState.Connected));
        _snapshots.Release();
        await StatusAsync(s => s.NextRefresh == s_t0 + s_live);
    }

    private async Task<MonitorStatus> StatusAsync(Func<MonitorStatus, bool> condition)
    {
        await TestClock.Until(() => condition(_monitor.Status), "the expected monitor status");
        return _monitor.Status;
    }
}
