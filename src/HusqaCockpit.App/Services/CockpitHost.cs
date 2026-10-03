using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;
using Microsoft.Extensions.Logging;

namespace HusqaCockpit.App.Services;

public sealed record CredentialTestResult(bool Success, string Message);

/// <summary>Owns the connection to Husqvarna: creates, restarts and stops the <see cref="FleetMonitor"/>.</summary>
public sealed class CockpitHost(CredentialStore credentialStore, AppSettings settings, ILoggerFactory loggers) : IAsyncDisposable
{
    private static readonly TimeSpan s_refreshAfterCommand = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ILogger _logger = loggers.CreateLogger<CockpitHost>();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private FleetMonitor? _monitor;

    /// <summary>Mower states; kept across restarts so the UI keeps its view models.</summary>
    public MowerFleet Fleet { get; } = new();

    public MonitorStatus Status => _monitor?.Status ?? new MonitorStatus(MonitorState.Stopped);

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
            var stream = new AutomowerEventStream(tokens, logger: loggers.CreateLogger<AutomowerEventStream>());
            var options = new FleetMonitorOptions
            {
                RefreshIntervalWhenPolling = TimeSpan.FromMinutes(Math.Clamp(settings.PollingIntervalMinutes, 5, 60)),
            };

            _monitor = new FleetMonitor(api, stream, Fleet, options, logger: loggers.CreateLogger<FleetMonitor>());
            _monitor.StatusChanged += OnMonitorStatusChanged;
            _monitor.Start();
            _logger.LogInformation("Monitoring started");
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

    public Task RefreshAsync() => _monitor?.RefreshNowAsync() ?? Task.CompletedTask;

    public Task SendActionAsync(string mowerId, MowerAction action) =>
        RunCommandAsync(api => api.SendActionAsync(mowerId, action));

    public Task SetCuttingHeightAsync(string mowerId, int height) =>
        RunCommandAsync(api => api.SetCuttingHeightAsync(mowerId, height));

    public Task SetHeadlightModeAsync(string mowerId, HeadlightMode mode) =>
        RunCommandAsync(api => api.SetHeadlightModeAsync(mowerId, mode));

    public Task ResetBladeUsageAsync(string mowerId) =>
        RunCommandAsync(api => api.ResetCuttingBladeUsageTimeAsync(mowerId));

    public Task ConfirmErrorAsync(string mowerId) =>
        RunCommandAsync(api => api.ConfirmErrorAsync(mowerId));

    public Task<IReadOnlyList<MowerMessage>> GetMessagesAsync(string mowerId) =>
        RequireMonitor().Api.GetMessagesAsync(mowerId);

    /// <summary>Checks a key/secret pair by logging in and listing the mowers.</summary>
    public async Task<CredentialTestResult> TestCredentialsAsync(ApiCredentials credentials)
    {
        try
        {
            var tokens = new ClientCredentialsTokenProvider(_http, credentials);
            var api = new AutomowerClient(_http, tokens);
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
            _logger.LogWarning(ex, "Credential test failed");
            return new CredentialTestResult(false, Loc.Format("Settings_TestError", ex.Message));
        }
    }

    private async Task RunCommandAsync(Func<AutomowerClient, Task> command)
    {
        var monitor = RequireMonitor();
        await command(monitor.Api).ConfigureAwait(false);

        // Without real-time events the change would only show at the next poll: refresh shortly.
        if (monitor.Status.State != MonitorState.Live)
        {
            _ = Task.Delay(s_refreshAfterCommand).ContinueWith(_ => monitor.RequestRefresh(), TaskScheduler.Default);
        }
    }

    private FleetMonitor RequireMonitor() =>
        _monitor ?? throw new InvalidOperationException(Loc.Get("Error_NotConnected"));

    private void OnMonitorStatusChanged(object? sender, MonitorStatus status) => StatusChanged?.Invoke(this, status);

    private async Task StopMonitorAsync()
    {
        if (_monitor is null)
        {
            return;
        }

        _monitor.StatusChanged -= OnMonitorStatusChanged;
        await _monitor.StopAsync().ConfigureAwait(false);
        _monitor = null;
        StatusChanged?.Invoke(this, Status);
    }
}
