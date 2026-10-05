using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Presentation.Tests.Support;

/// <summary>Runs everything at once, on the calling thread, which then counts as the UI thread.</summary>
internal sealed class InlineDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}

/// <summary>Records the commands instead of sending them; the fleet is a real one fed by the tests.</summary>
internal sealed class FakeCockpitHost : ICockpitHost
{
    public MowerFleet Fleet { get; } = new(TestMowers.Zone);

    public MonitorStatus Status { get; private set; } = new(MonitorState.Stopped);

    public bool HasCredentials { get; set; } = true;

    /// <summary>Commands received, such as "ParkUntilFurtherNotice mower-1".</summary>
    public List<string> Commands { get; } = [];

    public int Restarts { get; private set; }

    /// <summary>Mowers whose commands fail.</summary>
    public HashSet<string> Failing { get; } = [];

    public Exception? RefreshError { get; set; }

    public CredentialTestResult CredentialTest { get; set; } = new(true, "OK");

    public event EventHandler<MonitorStatus>? StatusChanged;

    public void SetStatus(MonitorStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    public Task RestartAsync()
    {
        Restarts++;
        return Task.CompletedTask;
    }

    public Task RefreshAsync() => RefreshError is null ? Task.CompletedTask : Task.FromException(RefreshError);

    public Task SendActionAsync(string mowerId, MowerAction action) => Record(mowerId, action.Type);

    public Task SetCuttingHeightAsync(string mowerId, int height) => Record(mowerId, $"CuttingHeight={height}");

    public Task SetHeadlightModeAsync(string mowerId, HeadlightMode mode) => Record(mowerId, $"Headlight={mode}");

    public Task SetCalendarAsync(string mowerId, IReadOnlyList<CalendarTask> tasks) => Record(mowerId, $"Calendar({tasks.Count})");

    public Task ResetBladeUsageAsync(string mowerId) => Record(mowerId, "ResetBladeUsage");

    public Task ConfirmErrorAsync(string mowerId) => Record(mowerId, "ConfirmError");

    public Task<IReadOnlyList<MowerMessage>> GetMessagesAsync(string mowerId) => Task.FromResult<IReadOnlyList<MowerMessage>>([]);

    public Task<CredentialTestResult> TestCredentialsAsync(ApiCredentials credentials) => Task.FromResult(CredentialTest);

    private Task Record(string mowerId, string command)
    {
        if (Failing.Contains(mowerId))
        {
            return Task.FromException(new InvalidOperationException("mower offline"));
        }
        Commands.Add($"{command} {mowerId}");
        return Task.CompletedTask;
    }
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    public int Saves { get; private set; }

    public void Save(AppSettings settings) => Saves++;
}

internal sealed class FakeCredentialStore : ICredentialStore
{
    public ApiCredentials? Stored { get; set; }

    public ApiCredentials? LoadCredentials() => Stored;

    public void SaveCredentials(ApiCredentials credentials) => Stored = credentials;

    public void DeleteCredentials() => Stored = null;
}

internal sealed class FakeShell : IAppShell
{
    public bool StartsWithWindows { get; set; }

    public List<string> Notifications { get; } = [];

    public int Restarts { get; private set; }

    public void ShowNotification(string title, string body) => Notifications.Add(title);

    public void Restart() => Restarts++;

    public void OpenLogsFolder()
    {
    }
}
