using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Presentation.ViewModels;

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
        (Start, End) = ScheduleSlotTimes.ToClockTimes(task.Start, task.Duration);
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

    // The labels below are instance properties because x:Bind only reaches instance members.

    /// <summary>"24HourClock" or "12HourClock", following the regional settings.</summary>
    public string ClockIdentifier { get; } =
        CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal) ? "24HourClock" : "12HourClock";

    public string MondayLabel { get; } = Label(0);
    public string TuesdayLabel { get; } = Label(1);
    public string WednesdayLabel { get; } = Label(2);
    public string ThursdayLabel { get; } = Label(3);
    public string FridayLabel { get; } = Label(4);
    public string SaturdayLabel { get; } = Label(5);
    public string SundayLabel { get; } = Label(6);

    public string MondayName { get; } = FullName(0);
    public string TuesdayName { get; } = FullName(1);
    public string WednesdayName { get; } = FullName(2);
    public string ThursdayName { get; } = FullName(3);
    public string FridayName { get; } = FullName(4);
    public string SaturdayName { get; } = FullName(5);
    public string SundayName { get; } = FullName(6);

    public CalendarTask ToTask()
    {
        var (start, duration) = ScheduleSlotTimes.FromClockTimes(Start, End);
        return new()
        {
            Start = start,
            Duration = duration,
            Monday = Monday,
            Tuesday = Tuesday,
            Wednesday = Wednesday,
            Thursday = Thursday,
            Friday = Friday,
            Saturday = Saturday,
            Sunday = Sunday,
        };
    }

    private static string Label(int index) =>
        CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)s_days[index]].TrimEnd('.');

    private static string FullName(int index) => CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)s_days[index]];
}

/// <summary>Edits a copy of a mower's schedule and validates it before it is sent.</summary>
public sealed partial class ScheduleEditorViewModel : ObservableObject
{
    private readonly IStrings _strings;

    public ScheduleEditorViewModel(IEnumerable<CalendarTask> tasks, IStrings strings)
    {
        _strings = strings;
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
        // A slot ending at midnight has End = 00:00, so it does not push the new slot to the end of the day (there is no room after it).
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

    private string Describe(ScheduleIssue issue)
    {
        var slot = issue.Index + 1;
        return issue.Kind switch
        {
            ScheduleIssueKind.NoDays => _strings.Format("Issue_NoDays", slot),
            ScheduleIssueKind.Overlap => _strings.Format(
                "Issue_Overlap", slot, issue.OtherIndex!.Value + 1,
                CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)issue.Day!.Value]),
            ScheduleIssueKind.CrossesMidnight => _strings.Format("Issue_CrossesMidnight", slot),
            _ => _strings.Format("Issue_EndBeforeStart", slot),
        };
    }
}
