using HusqaCockpit.Core.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HusqaCockpit.Core.Fleet;

public enum MonitorState
{
    Stopped,
    Starting,

    /// <summary>Real-time events are flowing.</summary>
    Live,

    /// <summary>Real-time events unavailable; the state is refreshed periodically over REST.</summary>
    Polling,

    /// <summary>The API key or secret was rejected.</summary>
    AuthenticationFailed,

    /// <summary>The Husqvarna servers cannot be reached.</summary>
    Offline,
}

public sealed record MonitorStatus(
    MonitorState State,
    EventStreamState? StreamState = null,
    DateTimeOffset? LastRefresh = null,
    DateTimeOffset? NextRefresh = null,
    string? Detail = null);

public sealed class FleetMonitorOptions
{
    /// <summary>Safety-net refresh while real-time events work (catches connectivity changes, which have no event).</summary>
    public TimeSpan RefreshIntervalWhenLive { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Refresh interval when real-time events are unavailable. Mind the 10,000 requests/month quota.</summary>
    public TimeSpan RefreshIntervalWhenPolling { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Minimum spacing between two automatic refreshes.</summary>
    public TimeSpan MinimumRefreshSpacing { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Keeps <see cref="MowerFleet"/> up to date: an initial REST snapshot, real-time events,
/// and periodic REST refreshes (frequent when events are unavailable).
/// </summary>
public sealed partial class FleetMonitor : IAsyncDisposable
{
    private static readonly TimeSpan[] s_failureBackoff =
    [
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
    ];

    private readonly IMowerSnapshotSource _snapshots;
    private readonly IAutomowerEventFeed _feed;
    private readonly FleetMonitorOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _statusLock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task _loops = Task.CompletedTask;
    private TaskCompletionSource _refreshSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private EventStreamStatus? _streamStatus;
    private DateTimeOffset? _lastRefresh;
    private DateTimeOffset? _nextRefresh;
    private Exception? _refreshError;
    private MonitorStatus _status = new(MonitorState.Stopped);

    public FleetMonitor(
        IMowerSnapshotSource snapshots,
        IAutomowerEventFeed feed,
        MowerFleet fleet,
        FleetMonitorOptions? options = null,
        TimeProvider? time = null,
        ILogger<FleetMonitor>? logger = null)
    {
        Fleet = fleet;
        _snapshots = snapshots;
        _feed = feed;
        _options = options ?? new FleetMonitorOptions();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<FleetMonitor>.Instance;
    }

    public MowerFleet Fleet { get; }

    public MonitorStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    public event EventHandler<MonitorStatus>? StatusChanged;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        PublishStatus();
        _loops = Task.WhenAll(
            Task.Run(() => RefreshLoopAsync(token), token),
            Task.Run(() => _feed.RunAsync(OnEventAsync, OnStreamStatus, token), token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        await _loops.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _cts.Dispose();
        _cts = null;
        lock (_statusLock)
        {
            _status = new MonitorStatus(MonitorState.Stopped);
        }
        PublishStatus();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Asks the background loop to refresh soon (respecting the minimum spacing).</summary>
    public void RequestRefresh() => _refreshSignal.TrySetResult();

    /// <summary>Refreshes immediately and returns when the fleet is updated.</summary>
    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resources = await _snapshots.GetMowerResourcesAsync(cancellationToken).ConfigureAwait(false);
            Fleet.ApplySnapshot(resources);
            lock (_statusLock)
            {
                _lastRefresh = _time.GetUtcNow();
                _refreshError = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_statusLock)
            {
                _refreshError = ex;
            }
            throw;
        }
        finally
        {
            _refreshGate.Release();
            PublishStatus();
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
                failures = 0;
                wait = IsStreamLive ? _options.RefreshIntervalWhenLive : _options.RefreshIntervalWhenPolling;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (AuthenticationException ex) when (ex.IsInvalidCredentials)
            {
                LogCredentialsRejected(_logger, ex.Message);
                wait = TimeSpan.FromMinutes(30);
            }
            catch (AuthenticationException ex) when (ex.IsTooManyLogins)
            {
                LogTooManyLogins(_logger);
                wait = TimeSpan.FromMinutes(1);
            }
            catch (Exception ex)
            {
                failures++;
                wait = s_failureBackoff[Math.Min(failures, s_failureBackoff.Length) - 1];
                LogRefreshFailed(_logger, ex, wait);
            }

            lock (_statusLock)
            {
                _nextRefresh = _time.GetUtcNow() + wait;
            }
            PublishStatus();

            if (!await WaitForNextRefreshAsync(wait, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <returns>False when cancelled.</returns>
    private async Task<bool> WaitForNextRefreshAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        try
        {
            var signal = _refreshSignal.Task;
            await Task.WhenAny(Task.Delay(wait, _time, cancellationToken), signal).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (signal.IsCompleted)
            {
                _refreshSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var sinceLast = _time.GetUtcNow() - (_lastRefresh ?? DateTimeOffset.MinValue);
                if (sinceLast < _options.MinimumRefreshSpacing)
                {
                    await Task.Delay(_options.MinimumRefreshSpacing - sinceLast, _time, cancellationToken).ConfigureAwait(false);
                }
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private ValueTask OnEventAsync(AutomowerEvent evt)
    {
        if (!Fleet.ApplyEvent(evt))
        {
            LogUnknownMower(_logger, evt.MowerId);
            RequestRefresh();
        }
        return ValueTask.CompletedTask;
    }

    private void OnStreamStatus(EventStreamStatus status)
    {
        var wasLive = IsStreamLive;
        lock (_statusLock)
        {
            _streamStatus = status;
        }

        if (status.State == EventStreamState.Connected && status.AfterOutage)
        {
            // Events may have been missed while disconnected.
            RequestRefresh();
        }
        else if (wasLive && status.State is EventStreamState.Disconnected or EventStreamState.Forbidden)
        {
            // Switch to the shorter polling interval right away.
            RequestRefresh();
        }
        PublishStatus();
    }

    private bool IsStreamLive
    {
        get
        {
            lock (_statusLock)
            {
                return _streamStatus?.State == EventStreamState.Connected;
            }
        }
    }

    private void PublishStatus()
    {
        MonitorStatus status;
        lock (_statusLock)
        {
            if (_cts is null)
            {
                return;
            }

            var streamState = _streamStatus?.State;
            status = _refreshError switch
            {
                AuthenticationException { IsInvalidCredentials: true } auth =>
                    new MonitorStatus(MonitorState.AuthenticationFailed, streamState, _lastRefresh, _nextRefresh, auth.Message),
                not null when streamState != EventStreamState.Connected =>
                    new MonitorStatus(MonitorState.Offline, streamState, _lastRefresh, _nextRefresh, _refreshError.Message),
                _ when streamState == EventStreamState.Connected =>
                    new MonitorStatus(MonitorState.Live, streamState, _lastRefresh, _nextRefresh),
                _ when _lastRefresh is not null =>
                    new MonitorStatus(MonitorState.Polling, streamState, _lastRefresh, _nextRefresh, _streamStatus?.Detail),
                _ => new MonitorStatus(MonitorState.Starting, streamState, _lastRefresh, _nextRefresh),
            };

            if (status == _status)
            {
                return;
            }
            _status = status;
        }
        StatusChanged?.Invoke(this, status);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credentials rejected: {Message}")]
    private static partial void LogCredentialsRejected(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Too many logins, waiting before retrying")]
    private static partial void LogTooManyLogins(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh failed, retrying in {Delay}")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Event for unknown mower {MowerId}, refreshing")]
    private static partial void LogUnknownMower(ILogger logger, string mowerId);
}
