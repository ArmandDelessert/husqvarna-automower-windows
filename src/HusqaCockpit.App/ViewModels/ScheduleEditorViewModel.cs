using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.App.Services;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.App.ViewModels;

/// <summary>One editable time slot of the weekly schedule.</summary>
public sealed partial class ScheduleRowViewModel : ObservableObject
{
    // Monday first, like the Automower Connect app.
    private static readonly DayOfWeek[] s_days =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public ScheduleRowViewModel(CalendarTask task)
    {
        Start = TimeSpan.FromMinutes(task.Start);
        End = TimeSpan.FromMinutes(task.Start + task.Duration);
        Monday = task.Monday;
        Tuesday = task.Tuesday;
        Wednesday = task.Wednesday;
        Thursday = task.Thursday;
        Friday = task.Friday;
        Saturday = task.Saturday;
        Sunday = task.Sunday;
    }

    [ObservableProperty]
    public partial TimeSpan Start { get; set; }

    [ObservableProperty]
    public partial TimeSpan End { get; set; }

    [ObservableProperty]
    public partial bool Monday { get; set; }

    [ObservableProperty]
    public partial bool Tuesday { get; set; }

    [ObservableProperty]
    public partial bool Wednesday { get; set; }

    [ObservableProperty]
    public partial bool Thursday { get; set; }

    [ObservableProperty]
    public partial bool Friday { get; set; }

    [ObservableProperty]
    public partial bool Saturday { get; set; }

    [ObservableProperty]
    public partial bool Sunday { get; set; }

    /// <summary>"24HourClock" or "12HourClock", following the regional settings.</summary>
    public string ClockIdentifier =>
        CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal) ? "24HourClock" : "12HourClock";

    public string MondayLabel => Label(0);
    public string TuesdayLabel => Label(1);
    public string WednesdayLabel => Label(2);
    public string ThursdayLabel => Label(3);
    public string FridayLabel => Label(4);
    public string SaturdayLabel => Label(5);
    public string SundayLabel => Label(6);

    public string MondayName => FullName(0);
    public string TuesdayName => FullName(1);
    public string WednesdayName => FullName(2);
    public string ThursdayName => FullName(3);
    public string FridayName => FullName(4);
    public string SaturdayName => FullName(5);
    public string SundayName => FullName(6);

    public CalendarTask ToTask() => new()
    {
        Start = (int)Start.TotalMinutes,
        Duration = (int)(End - Start).TotalMinutes,
        Monday = Monday,
        Tuesday = Tuesday,
        Wednesday = Wednesday,
        Thursday = Thursday,
        Friday = Friday,
        Saturday = Saturday,
        Sunday = Sunday,
    };

    private static string Label(int index) =>
        CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)s_days[index]].TrimEnd('.');

    private static string FullName(int index) => CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)s_days[index]];
}

/// <summary>Edits a copy of a mower's schedule and validates it before it is sent.</summary>
public sealed partial class ScheduleEditorViewModel : ObservableObject
{
    public ScheduleEditorViewModel(IEnumerable<CalendarTask> tasks)
    {
        foreach (var task in tasks.OrderBy(t => t.Start))
        {
            Rows.Add(Track(new ScheduleRowViewModel(task)));
        }
        Rows.CollectionChanged += OnRowsChanged;
        Revalidate();
    }

    public ObservableCollection<ScheduleRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsValid { get; set; } = true;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public IReadOnlyList<CalendarTask> ToTasks() => Rows.Select(r => r.ToTask()).ToList();

    [RelayCommand]
    private void AddRow()
    {
        // Default: starts at the next full hour after the last slot, lasts 2 hours, on weekdays.
        var last = Rows.Count > 0 ? Rows.Max(r => r.End) : TimeSpan.FromHours(7);
        var start = TimeSpan.FromHours(Math.Min(Math.Ceiling(last.TotalHours), 21));
        Rows.Add(Track(new ScheduleRowViewModel(new CalendarTask
        {
            Start = (int)start.TotalMinutes,
            Duration = 120,
            Monday = true,
            Tuesday = true,
            Wednesday = true,
            Thursday = true,
            Friday = true,
        })));
    }

    public void RemoveRow(ScheduleRowViewModel row) => Rows.Remove(row);

    private ScheduleRowViewModel Track(ScheduleRowViewModel row)
    {
        row.PropertyChanged += OnRowPropertyChanged;
        return row;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ScheduleRowViewModel row in e.OldItems)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }
        }
        Revalidate();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => Revalidate();

    private void Revalidate()
    {
        IsEmpty = Rows.Count == 0;
        var issues = ScheduleValidator.Validate(ToTasks());
        IsValid = issues.Count == 0;
        HasError = issues.Count > 0;
        ErrorText = issues.Count == 0 ? "" : Describe(issues[0]);
    }

    private static string Describe(ScheduleIssue issue)
    {
        var slot = issue.Index + 1;
        return issue.Kind switch
        {
            ScheduleIssueKind.NoDays => Loc.Format("Issue_NoDays", slot),
            ScheduleIssueKind.Overlap => Loc.Format(
                "Issue_Overlap", slot, issue.OtherIndex!.Value + 1,
                CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)issue.Day!.Value]),
            ScheduleIssueKind.CrossesMidnight => Loc.Format("Issue_CrossesMidnight", slot),
            _ => Loc.Format("Issue_EndBeforeStart", slot),
        };
    }
}
