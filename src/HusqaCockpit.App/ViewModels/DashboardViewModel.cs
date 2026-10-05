using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.App.Services;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;
using HusqaCockpit.Presentation;

namespace HusqaCockpit.App.ViewModels;

/// <summary>State of the whole fleet and of the connection. Lives as long as the app.</summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan s_tickInterval = TimeSpan.FromSeconds(30);

    private readonly CockpitHost _host;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStrings _strings;
    private readonly TimeProvider _time;
    private readonly ITimer _ticker;

    public DashboardViewModel(CockpitHost host, IUiDispatcher dispatcher, IStrings strings, TimeProvider time)
    {
        _host = host;
        _dispatcher = dispatcher;
        _strings = strings;
        _time = time;
        _host.Fleet.MowerChanged += (_, e) => OnUiThread(() => OnMowerChanged(e.Current));
        _host.Fleet.MowerRemoved += (_, e) => OnUiThread(() => OnMowerRemoved(e.Mower));
        _host.StatusChanged += (_, _) => OnUiThread(UpdateStatus);

        // Keeps "updated 3 min ago" texts current.
        _ticker = time.CreateTimer(_ => OnUiThread(Tick), null, s_tickInterval, s_tickInterval);
        UpdateStatus();
    }

    public ObservableCollection<MowerViewModel> Mowers { get; } = [];

    /// <summary>Raised when the overall picture changes (counts, errors): used for the tray icon.</summary>
    public event EventHandler? SummaryChanged;

    [ObservableProperty]
    public partial bool IsStatusVisible { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial StatusSeverity StatusSeverity { get; set; }

    /// <summary>The status bar offers a button to open the settings.</summary>
    [ObservableProperty]
    public partial bool StatusNeedsSettings { get; set; }

    [ObservableProperty]
    public partial string ConnectionLabel { get; set; } = "";

    [ObservableProperty]
    public partial string ConnectionGlyph { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? CommandMessage { get; set; }

    public bool HasCommandMessage => !string.IsNullOrEmpty(CommandMessage);

    partial void OnCommandMessageChanged(string? value) => OnPropertyChanged(nameof(HasCommandMessage));

    public int ErrorCount => Mowers.Count(m => m.HasError);

    public MowerViewModel? Find(string id) => Mowers.FirstOrDefault(m => m.Id == id);

    public void Dispose() => _ticker.Dispose();

    /// <summary>One-line summary for the tray tooltip, e.g. "3 mowers · 1 mowing · 1 error".</summary>
    public string TraySummary
    {
        get
        {
            if (Mowers.Count == 0)
            {
                return _strings.Text("AppName");
            }
            var mowing = Mowers.Count(m => m.Mower.Activity == MowerActivity.Mowing);
            var text = $"{_strings.Text("AppName")}\n{_strings.Format("Tray_Summary", Mowers.Count, mowing)}";
            return ErrorCount > 0 ? $"{text}\n{_strings.Format("Tray_Errors", ErrorCount)}" : text;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await _host.RefreshAsync();
            CommandMessage = null;
        }
        catch (Exception ex)
        {
            CommandMessage = _strings.Format("Command_Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ParkAllAsync() => RunForAllAsync(MowerAction.ParkUntilFurtherNotice(), "Command_ParkAllSent");

    [RelayCommand]
    private Task ResumeAllAsync() => RunForAllAsync(MowerAction.ResumeSchedule(), "Command_ResumeAllSent");

    private async Task RunForAllAsync(MowerAction action, string successKey)
    {
        IsBusy = true;
        var failures = new List<string>();
        try
        {
            foreach (var mower in Mowers.ToList())
            {
                try
                {
                    await _host.SendActionAsync(mower.Id, action);
                }
                catch (Exception ex)
                {
                    failures.Add($"{mower.Name} ({ex.Message})");
                }
            }
            CommandMessage = failures.Count == 0
                ? _strings.Text(successKey)
                : _strings.Format("Command_FailedFor", string.Join(", ", failures));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnMowerChanged(Mower mower)
    {
        var existing = Find(mower.Id);
        if (existing is not null)
        {
            existing.Update(mower);
        }
        else
        {
            var viewModel = new MowerViewModel(mower, _host, _strings, _time);
            var index = 0;
            while (index < Mowers.Count && StringComparer.CurrentCultureIgnoreCase.Compare(Mowers[index].Name, mower.Name) < 0)
            {
                index++;
            }
            Mowers.Insert(index, viewModel);
        }
        IsEmpty = Mowers.Count == 0;
        SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMowerRemoved(Mower mower)
    {
        if (Find(mower.Id) is { } viewModel)
        {
            Mowers.Remove(viewModel);
        }
        IsEmpty = Mowers.Count == 0;
        SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStatus()
    {
        var status = _host.Status;
        StatusNeedsSettings = false;
        (ConnectionGlyph, ConnectionLabel) = status.State switch
        {
            MonitorState.Live => ("", _strings.Text("Connection_Live")),
            MonitorState.Polling => ("", _strings.Text("Connection_Polling")),
            MonitorState.Starting => ("", _strings.Text("Connection_Starting")),
            MonitorState.Stopped when !_host.HasCredentials => ("", _strings.Text("Connection_NotConfigured")),
            _ => ("", _strings.Text("Connection_Offline")),
        };

        switch (status.State)
        {
            case MonitorState.Stopped when !_host.HasCredentials:
                Show(StatusSeverity.Warning, _strings.Text("Banner_NoCredentialsTitle"), _strings.Text("Banner_NoCredentials"), needsSettings: true);
                break;
            case MonitorState.Stopped:
            case MonitorState.Live:
                IsStatusVisible = false;
                break;
            case MonitorState.Starting:
                Show(StatusSeverity.Info, _strings.Text("Banner_StartingTitle"), "");
                break;
            case MonitorState.Polling:
                var interval = status.NextRefresh is { } next && status.LastRefresh is { } last
                    ? _strings.Format("Banner_PollingTimes", Time(last), Time(next))
                    : "";
                if (status.StreamState == EventStreamState.Forbidden)
                {
                    Show(StatusSeverity.Warning, _strings.Text("Banner_PollingTitle"), $"{_strings.Text("Banner_Forbidden")} {interval}".Trim());
                }
                else
                {
                    Show(StatusSeverity.Info, _strings.Text("Banner_PollingTitle"), $"{_strings.Text("Banner_Polling")} {interval}".Trim());
                }
                break;
            case MonitorState.AuthenticationFailed:
                Show(StatusSeverity.Error, _strings.Text("Banner_AuthFailedTitle"), _strings.Text("Banner_AuthFailed"), needsSettings: true);
                break;
            case MonitorState.Offline:
                var retry = status.NextRefresh is { } at ? _strings.Format("Banner_RetryAt", Time(at)) : "";
                Show(StatusSeverity.Error, _strings.Text("Banner_OfflineTitle"), $"{status.Detail} {retry}".Trim());
                break;
        }
    }

    private void Tick()
    {
        foreach (var mower in Mowers)
        {
            mower.Tick();
        }
        UpdateStatus();
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread: at once when already there, queued otherwise.</summary>
    private void OnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    private void Show(StatusSeverity severity, string title, string message, bool needsSettings = false)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        StatusNeedsSettings = needsSettings;
        IsStatusVisible = true;
    }

    private static string Time(DateTimeOffset moment) => moment.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
}
