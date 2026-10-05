using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;
using HusqaCockpit.Presentation;
using Microsoft.Extensions.Logging;

namespace HusqaCockpit.App.Services;

/// <summary>Owns the connection to Husqvarna: creates, restarts and stops the <see cref="FleetMonitor"/>.</summary>
public sealed partial class CockpitHost(CredentialStore credentialStore, AppSettings settings, ILoggerFactory loggers) : ICockpitHost, IAsyncDisposable
{
    private static readonly TimeSpan s_refreshAfterCommand = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ILogger _logger = loggers.CreateLogger<CockpitHost>();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Session? _session;

    /// <summary>Mower states; kept across restarts so the UI keeps its view models.</summary>
    public MowerFleet Fleet { get; } = new();

    public MonitorStatus Status => _session?.Monitor.Status ?? new MonitorStatus(MonitorState.Stopped);

    public bool HasCredentials => credentialStore.LoadCredentials() is not null;

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler<MonitorStatus>? StatusChanged;

    /// <summary>Raised for every request sent to the Automower API (background thread).</summary>
    public event EventHandler? RequestSent;

    /// <summary>(Re)starts monitoring with the stored credentials. Does nothing when none are stored.</summary>
    public async Task RestartAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopMonitorAsync().ConfigureAwait(false);

            var credentials = credentialStore.LoadCredentials();
            if (credentials is null)
            {
                Fleet.ApplySnapshot([]);
                StatusChanged?.Invoke(this, Status);
                return;
            }

            var tokens = new ClientCredentialsTokenProvider(_http, credentials, credentialStore, logger: loggers.CreateLogger<ClientCredentialsTokenProvider>());
            var api = new AutomowerClient(_http, tokens, logger: loggers.CreateLogger<AutomowerClient>());
            api.RequestSent += (_, _) => RequestSent?.Invoke(this, EventArgs.Empty);
            var feed = new AutomowerEventFeed(tokens, logger: loggers.CreateLogger<AutomowerEventFeed>());
            var options = new FleetMonitorOptions
            {
                RefreshIntervalWhenPolling = TimeSpan.FromMinutes(Math.Clamp(settings.PollingIntervalMinutes, 5, 60)),
            };

            var monitor = new FleetMonitor(api, feed, Fleet, options, logger: loggers.CreateLogger<FleetMonitor>());
            monitor.StatusChanged += OnMonitorStatusChanged;
            _session = new Session(monitor, api, tokens);
            monitor.Start();
            LogMonitoringStarted(_logger);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopMonitorAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    public Task RefreshAsync() => _session?.Monitor.RefreshNowAsync() ?? Task.CompletedTask;

    public Task SendActionAsync(string mowerId, MowerAction action) =>
        RunCommandAsync(api => api.SendActionAsync(mowerId, action));

    public Task SetCuttingHeightAsync(string mowerId, int height) =>
        RunCommandAsync(api => api.SetCuttingHeightAsync(mowerId, height));

    public Task SetHeadlightModeAsync(string mowerId, HeadlightMode mode) =>
        RunCommandAsync(api => api.SetHeadlightModeAsync(mowerId, mode));

    public Task SetCalendarAsync(string mowerId, IReadOnlyList<CalendarTask> tasks) =>
        RunCommandAsync(api => api.SetCalendarAsync(mowerId, tasks));

    public Task ResetBladeUsageAsync(string mowerId) =>
        RunCommandAsync(api => api.ResetCuttingBladeUsageTimeAsync(mowerId));

    public Task ConfirmErrorAsync(string mowerId) =>
        RunCommandAsync(api => api.ConfirmErrorAsync(mowerId));

    public Task<IReadOnlyList<MowerMessage>> GetMessagesAsync(string mowerId) =>
        RequireSession().Api.GetMessagesAsync(mowerId);

    /// <summary>Checks a key/secret pair by logging in and listing the mowers.</summary>
    public async Task<CredentialTestResult> TestCredentialsAsync(ApiCredentials credentials)
    {
        try
        {
            using var tokens = new ClientCredentialsTokenProvider(_http, credentials);
            using var api = new AutomowerClient(_http, tokens);
            var mowers = await api.GetMowerResourcesAsync().ConfigureAwait(false);
            RequestSent?.Invoke(this, EventArgs.Empty);
            return new CredentialTestResult(true, Loc.Format("Settings_TestSuccess", mowers.Count));
        }
        catch (AuthenticationException ex) when (ex.IsTooManyLogins)
        {
            return new CredentialTestResult(false, Loc.Get("Settings_TestTooManyLogins"));
        }
        catch (AuthenticationException ex) when (ex.IsInvalidCredentials)
        {
            return new CredentialTestResult(false, Loc.Get("Settings_TestInvalid"));
        }
        catch (AutomowerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return new CredentialTestResult(false, Loc.Get("Settings_TestForbidden"));
        }
        catch (Exception ex)
        {
            LogCredentialTestFailed(_logger, ex);
            return new CredentialTestResult(false, Loc.Format("Settings_TestError", ex.Message));
        }
    }

    private async Task RunCommandAsync(Func<AutomowerClient, Task> command)
    {
        var session = RequireSession();
        await command(session.Api).ConfigureAwait(false);

        // Without real-time events the change would only show at the next poll: refresh shortly.
        if (session.Monitor.Status.State != MonitorState.Live)
        {
            _ = Task.Delay(s_refreshAfterCommand).ContinueWith(_ => session.Monitor.RequestRefresh(), TaskScheduler.Default);
        }
    }

    private Session RequireSession() =>
        _session ?? throw new InvalidOperationException(Loc.Get("Error_NotConnected"));

    private void OnMonitorStatusChanged(object? sender, MonitorStatus status) => StatusChanged?.Invoke(this, status);

    private async Task StopMonitorAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        session.Monitor.StatusChanged -= OnMonitorStatusChanged;
        await session.Monitor.StopAsync().ConfigureAwait(false);
        session.Api.Dispose();
        session.Tokens.Dispose();
        _session = null;
        StatusChanged?.Invoke(this, Status);
    }

    /// <summary>A running monitor, with the client and the token provider created for it alone.</summary>
    private sealed record Session(FleetMonitor Monitor, AutomowerClient Api, ClientCredentialsTokenProvider Tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitoring started")]
    private static partial void LogMonitoringStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credential test failed")]
    private static partial void LogCredentialTestFailed(ILogger logger, Exception exception);
}
