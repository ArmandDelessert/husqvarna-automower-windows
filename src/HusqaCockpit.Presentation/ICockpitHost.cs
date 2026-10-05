using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Presentation;

public sealed record CredentialTestResult(bool Success, string Message);

/// <summary>
/// The connection to Husqvarna, as the view models see it: the fleet, the monitoring status and the commands.
/// The app implements it with CockpitHost; the tests with a fake.
/// </summary>
public interface ICockpitHost
{
    /// <summary>Mower states; kept across restarts so the UI keeps its view models.</summary>
    MowerFleet Fleet { get; }

    MonitorStatus Status { get; }

    bool HasCredentials { get; }

    /// <summary>Raised on a background thread.</summary>
    event EventHandler<MonitorStatus>? StatusChanged;

    /// <summary>(Re)starts monitoring with the stored credentials. Does nothing when none are stored.</summary>
    Task RestartAsync();

    Task RefreshAsync();

    Task SendActionAsync(string mowerId, MowerAction action);

    Task SetCuttingHeightAsync(string mowerId, int height);

    Task SetHeadlightModeAsync(string mowerId, HeadlightMode mode);

    Task SetCalendarAsync(string mowerId, IReadOnlyList<CalendarTask> tasks);

    Task ResetBladeUsageAsync(string mowerId);

    Task ConfirmErrorAsync(string mowerId);

    Task<IReadOnlyList<MowerMessage>> GetMessagesAsync(string mowerId);

    /// <summary>Checks a key/secret pair by logging in and listing the mowers.</summary>
    Task<CredentialTestResult> TestCredentialsAsync(ApiCredentials credentials);
}
