using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.App.Services;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;
using Microsoft.UI.Dispatching;

namespace HusqaCockpit.App.ViewModels;

/// <summary>State of the whole fleet and of the connection. Lives as long as the app.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly CockpitHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _ticker;

    public DashboardViewModel(CockpitHost host, DispatcherQueue dispatcher)
    {
        _host = host;
        _dispatcher = dispatcher;
        _host.Fleet.MowerChanged += (_, e) => _dispatcher.TryEnqueue(() => OnMowerChanged(e.Current));
        _host.Fleet.MowerRemoved += (_, e) => _dispatcher.TryEnqueue(() => OnMowerRemoved(e.Mower));
        _host.StatusChanged += (_, _) => _dispatcher.TryEnqueue(UpdateStatus);

        // Keeps "updated 3 min ago" texts current.
        _ticker = dispatcher.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(30);
        _ticker.Tick += (_, _) =>
        {
            foreach (var mower in Mowers)
            {
                mower.Tick();
            }
            UpdateStatus();
        };
        _ticker.Start();
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

    /// <summary>One-line summary for the tray tooltip, e.g. "3 mowers · 1 mowing · 1 error".</summary>
    public string TraySummary
    {
        get
        {
            if (Mowers.Count == 0)
            {
                return Loc.Get("AppName");
            }
            var mowing = Mowers.Count(m => m.Mower.Activity == MowerActivity.Mowing);
            var text = $"{Loc.Get("AppName")}\n{Loc.Format("Tray_Summary", Mowers.Count, mowing)}";
            return ErrorCount > 0 ? $"{text}\n{Loc.Format("Tray_Errors", ErrorCount)}" : text;
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
            CommandMessage = Loc.Format("Command_Failed", ex.Message);
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
                ? Loc.Get(successKey)
                : Loc.Format("Command_FailedFor", string.Join(", ", failures));
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
            var viewModel = new MowerViewModel(mower, _host);
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
            MonitorState.Live => ("", Loc.Get("Connection_Live")),
            MonitorState.Polling => ("", Loc.Get("Connection_Polling")),
            MonitorState.Starting => ("", Loc.Get("Connection_Starting")),
            MonitorState.Stopped when !_host.HasCredentials => ("", Loc.Get("Connection_NotConfigured")),
            _ => ("", Loc.Get("Connection_Offline")),
        };

        switch (status.State)
        {
            case MonitorState.Stopped when !_host.HasCredentials:
                Show(StatusSeverity.Warning, Loc.Get("Banner_NoCredentialsTitle"), Loc.Get("Banner_NoCredentials"), needsSettings: true);
                break;
            case MonitorState.Stopped:
            case MonitorState.Live:
                IsStatusVisible = false;
                break;
            case MonitorState.Starting:
                Show(StatusSeverity.Info, Loc.Get("Banner_StartingTitle"), "");
                break;
            case MonitorState.Polling:
                var interval = status.NextRefresh is { } next && status.LastRefresh is { } last
                    ? Loc.Format("Banner_PollingTimes", Time(last), Time(next))
                    : "";
                if (status.StreamState == EventStreamState.Forbidden)
                {
                    Show(StatusSeverity.Warning, Loc.Get("Banner_PollingTitle"), $"{Loc.Get("Banner_Forbidden")} {interval}".Trim());
                }
                else
                {
                    Show(StatusSeverity.Info, Loc.Get("Banner_PollingTitle"), $"{Loc.Get("Banner_Polling")} {interval}".Trim());
                }
                break;
            case MonitorState.AuthenticationFailed:
                Show(StatusSeverity.Error, Loc.Get("Banner_AuthFailedTitle"), Loc.Get("Banner_AuthFailed"), needsSettings: true);
                break;
            case MonitorState.Offline:
                var retry = status.NextRefresh is { } at ? Loc.Format("Banner_RetryAt", Time(at)) : "";
                Show(StatusSeverity.Error, Loc.Get("Banner_OfflineTitle"), $"{status.Detail} {retry}".Trim());
                break;
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
