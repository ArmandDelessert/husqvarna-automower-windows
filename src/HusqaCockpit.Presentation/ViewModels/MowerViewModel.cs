using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Presentation.ViewModels;

public sealed record ScheduleItem(string Days, string Hours);

public sealed record StatisticItem(string Label, string Value);

public sealed record MessageItem(string When, string Text, string Severity, string Glyph);

/// <summary>One mower card / detail page. All members must be used on the UI thread.</summary>
public sealed partial class MowerViewModel : ObservableObject
{
    public static readonly IReadOnlyList<HeadlightMode> HeadlightModes =
        [HeadlightMode.AlwaysOn, HeadlightMode.AlwaysOff, HeadlightMode.EveningOnly, HeadlightMode.EveningAndNight];

    private readonly ICockpitHost _host;
    private readonly IStrings _strings;
    private readonly MowerFormatter _formatter;
    private readonly TimeProvider _time;
    private Mower _mower;

    public MowerViewModel(Mower mower, ICockpitHost host, IStrings strings, TimeProvider time)
    {
        _host = host;
        _strings = strings;
        _time = time;
        _formatter = new MowerFormatter(strings);
        _mower = mower;
        Id = mower.Id;
        HeadlightModeNames = HeadlightModes.Select(m => strings.EnumText(m)).ToList();
        ResetDrafts();
    }

    public string Id { get; }
    public Mower Mower => _mower;

    // ----- Identity -----
    public string Name => _mower.Name;
    public string Model => ShortModel(_mower.Model);
    public string SerialNumber => _mower.Attributes.System.SerialNumber.ToString(CultureInfo.InvariantCulture);

    // ----- Status -----
    private StatusSummary Summary => _formatter.Summarize(_mower, _time.GetLocalNow());
    public string StatusTitle => Summary.Title;
    public string StatusDetail => Summary.Detail;
    public StatusSeverity Severity => Summary.Severity;
    public bool HasError => _mower.HasError;
    public bool IsConnected => _mower.IsConnected;
    public bool CanConfirmError => _mower.CanConfirmError;
    public string ErrorText => _mower.ErrorCode != 0 ? _strings.Format("Mower_ErrorCode", _mower.ErrorCode, _strings.ErrorCode(_mower.ErrorCode)) : "";
    public string ActivityText => _strings.EnumText(_mower.Activity);
    public string StateText => _strings.EnumText(_mower.State);
    public string ModeText => _strings.EnumText(_mower.Mode);

    public string LastSeenText => _mower.LastStatusTime is { } seen
        ? _strings.Format("Mower_Updated", _formatter.Relative(seen, _time.GetLocalNow()))
        : "";

    // ----- Battery -----
    public int BatteryPercent => _mower.BatteryPercent;
    public string BatteryText => string.Format(CultureInfo.CurrentCulture, "{0} %", _mower.BatteryPercent);
    public string BatteryGlyph => BatteryGlyphFor(_mower.BatteryPercent, _mower.Activity == MowerActivity.Charging);

    // ----- Settings -----
    public bool HasHeadlights => _mower.Attributes.Capabilities.Headlights;
    public string CuttingHeightText => _mower.Attributes.Settings.CuttingHeight?.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string HeadlightText => _strings.EnumText(_mower.Attributes.Settings.Headlight.Mode);
    public IReadOnlyList<string> HeadlightModeNames { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCuttingHeightChanged))]
    public partial double CuttingHeightDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHeadlightChanged))]
    public partial int HeadlightDraftIndex { get; set; }

    public bool IsCuttingHeightChanged => (int)CuttingHeightDraft != (_mower.Attributes.Settings.CuttingHeight ?? 0);
    public bool IsHeadlightChanged => HeadlightDraftIndex >= 0 && HeadlightModes[HeadlightDraftIndex] != _mower.Attributes.Settings.Headlight.Mode;

    // ----- Schedule, statistics, position -----
    public IReadOnlyList<ScheduleItem> Schedule => _mower.Attributes.Calendar.Tasks
        .Select(t => new ScheduleItem(_formatter.Days(t), MowerFormatter.TimeRange(t)))
        .ToList();

    public bool HasSchedule => _mower.Attributes.Calendar.Tasks.Count > 0;

    /// <summary>Mowers with work areas use a different schedule endpoint, not supported yet.</summary>
    public bool CanEditSchedule => !_mower.Attributes.Capabilities.WorkAreas;

    public bool CannotEditSchedule => !CanEditSchedule;

    public IReadOnlyList<CalendarTask> CurrentTasks => _mower.Attributes.Calendar.Tasks;

    public Task SaveScheduleAsync(IReadOnlyList<CalendarTask> tasks) =>
        RunAsync(() => _host.SetCalendarAsync(Id, tasks), "Command_ScheduleSent");

    public IReadOnlyList<StatisticItem> Statistics
    {
        get
        {
            var s = _mower.Attributes.Statistics;
            var items = new List<StatisticItem>
            {
                new(_strings.Text("Stats_CuttingTime"), _formatter.Hours(s.TotalCuttingTime)),
                new(_strings.Text("Stats_RunningTime"), _formatter.Hours(s.TotalRunningTime)),
                new(_strings.Text("Stats_ChargingTime"), _formatter.Hours(s.TotalChargingTime)),
                new(_strings.Text("Stats_SearchingTime"), _formatter.Hours(s.TotalSearchingTime)),
                new(_strings.Text("Stats_ChargingCycles"), MowerFormatter.Count(s.NumberOfChargingCycles)),
                new(_strings.Text("Stats_Collisions"), MowerFormatter.Count(s.NumberOfCollisions)),
                new(_strings.Text("Stats_Distance"), _formatter.Kilometers(s.TotalDriveDistance)),
            };
            if (s.CuttingBladeUsageTime is not null)
            {
                items.Add(new(_strings.Text("Stats_BladeUsage"), _formatter.Hours(s.CuttingBladeUsageTime)));
            }
            return items;
        }
    }

    public bool HasPosition => _mower.LastPosition is not null;

    public string PositionText => _mower.LastPosition is { } p
        ? string.Format(CultureInfo.CurrentCulture, "{0:F6}, {1:F6}", p.Latitude, p.Longitude)
        : _strings.Text("Mower_NoPosition");

    public Uri? MapUri => _mower.LastPosition is { } p
        ? new Uri(string.Format(CultureInfo.InvariantCulture,
            "https://www.openstreetmap.org/?mlat={0}&mlon={1}#map=19/{0}/{1}", p.Latitude, p.Longitude))
        : null;

    // ----- Messages -----
    public ObservableCollection<MessageItem> Messages { get; } = [];

    [ObservableProperty]
    public partial bool MessagesLoaded { get; set; }

    // ----- Command feedback -----
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? CommandMessage { get; set; }

    [ObservableProperty]
    public partial bool CommandFailed { get; set; }

    public bool HasCommandMessage => !string.IsNullOrEmpty(CommandMessage);

    partial void OnCommandMessageChanged(string? value) => OnPropertyChanged(nameof(HasCommandMessage));

    /// <summary>Applies a new snapshot from the API.</summary>
    public void Update(Mower mower)
    {
        var settingsChanged = mower.Attributes.Settings != _mower.Attributes.Settings;
        _mower = mower;
        if (settingsChanged)
        {
            ResetDrafts();
        }
        // Empty name: every binding on this object is refreshed.
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Re-evaluates time-relative texts ("5 min ago").</summary>
    public void Tick()
    {
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(LastSeenText));
    }

    private void ResetDrafts()
    {
        CuttingHeightDraft = _mower.Attributes.Settings.CuttingHeight ?? 1;
        HeadlightDraftIndex = HeadlightModes.ToList().IndexOf(_mower.Attributes.Settings.Headlight.Mode);
    }

    // ----- Commands -----

    [RelayCommand]
    private Task StartAsync(string minutes) =>
        RunAsync(() => _host.SendActionAsync(Id, MowerAction.Start(Minutes(minutes))), "Command_StartSent");

    [RelayCommand]
    private Task PauseAsync() => RunAsync(() => _host.SendActionAsync(Id, MowerAction.Pause()), "Command_PauseSent");

    [RelayCommand]
    private Task ResumeScheduleAsync() =>
        RunAsync(() => _host.SendActionAsync(Id, MowerAction.ResumeSchedule()), "Command_ResumeSent");

    [RelayCommand]
    private Task ParkUntilNextScheduleAsync() =>
        RunAsync(() => _host.SendActionAsync(Id, MowerAction.ParkUntilNextSchedule()), "Command_ParkSent");

    [RelayCommand]
    private Task ParkUntilFurtherNoticeAsync() =>
        RunAsync(() => _host.SendActionAsync(Id, MowerAction.ParkUntilFurtherNotice()), "Command_ParkSent");

    [RelayCommand]
    private Task ParkForAsync(string minutes) =>
        RunAsync(() => _host.SendActionAsync(Id, MowerAction.Park(Minutes(minutes))), "Command_ParkSent");

    [RelayCommand]
    private Task ConfirmErrorAsync() => RunAsync(() => _host.ConfirmErrorAsync(Id), "Command_ConfirmSent");

    [RelayCommand]
    private Task ApplyCuttingHeightAsync() =>
        RunAsync(() => _host.SetCuttingHeightAsync(Id, (int)CuttingHeightDraft), "Command_SettingsSent");

    [RelayCommand]
    private Task ApplyHeadlightAsync() =>
        HeadlightDraftIndex < 0
            ? Task.CompletedTask
            : RunAsync(() => _host.SetHeadlightModeAsync(Id, HeadlightModes[HeadlightDraftIndex]), "Command_SettingsSent");

    [RelayCommand]
    private Task ResetBladeUsageAsync() => RunAsync(() => _host.ResetBladeUsageAsync(Id), "Command_BladeResetSent");

    [RelayCommand]
    private async Task LoadMessagesAsync()
    {
        IsBusy = true;
        try
        {
            var messages = await _host.GetMessagesAsync(Id);
            Messages.Clear();
            foreach (var message in messages.OrderByDescending(m => m.Time).Take(50))
            {
                var when = MowerTime.FromMowerLocal(message.Time, _mower.TimeZone);
                Messages.Add(new MessageItem(
                    when?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "",
                    _strings.ErrorCode(message.Code),
                    _strings.EnumText(message.Severity),
                    message.Severity is MessageSeverity.Error or MessageSeverity.Fatal ? "" : ""));
            }
            MessagesLoaded = true;
            CommandMessage = null;
        }
        catch (Exception ex)
        {
            CommandFailed = true;
            CommandMessage = _strings.Format("Command_Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAsync(Func<Task> action, string successKey)
    {
        IsBusy = true;
        CommandMessage = null;
        try
        {
            await action();
            CommandFailed = false;
            CommandMessage = _strings.Text(successKey);
        }
        catch (Exception ex)
        {
            CommandFailed = true;
            CommandMessage = _strings.Format("Command_Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static TimeSpan Minutes(string minutes) =>
        TimeSpan.FromMinutes(int.Parse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture));

    private static string ShortModel(string model)
    {
        // "HUSQVARNA AUTOMOWER® 450X" / "Husqvarna Automower® 450X" → "Automower® 450X"
        var trimmed = model.Trim();
        if (trimmed.StartsWith("Husqvarna ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Husqvarna ".Length..];
        }
        return trimmed.Replace("AUTOMOWER", "Automower", StringComparison.Ordinal);
    }

    private static string BatteryGlyphFor(int percent, bool charging)
    {
        // Segoe Fluent Icons: Battery0–9 = E850–E859, Battery10 = E83F; BatteryCharging0–9 = E85A–E863, BatteryCharging10 = E83E.
        var level = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        if (level == 10)
        {
            return charging ? "" : "";
        }
        return ((char)((charging ? 0xE85A : 0xE850) + level)).ToString();
    }
}
