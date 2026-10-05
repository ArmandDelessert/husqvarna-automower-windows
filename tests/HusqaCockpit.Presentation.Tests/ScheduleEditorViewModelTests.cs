using HusqaCockpit.Core.Models;
using HusqaCockpit.Presentation.Tests.Support;
using HusqaCockpit.Presentation.ViewModels;

namespace HusqaCockpit.Presentation.Tests;

public sealed class ScheduleEditorViewModelTests : IDisposable
{
    private static readonly CalendarTask s_weekdays = new()
    {
        Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true,
    };

    private static readonly CalendarTask s_everyDay = s_weekdays with { Saturday = true, Sunday = true };

    private readonly IDisposable _culture = TestCulture.Use(TestCulture.English);

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void Slots_are_listed_by_start_and_come_back_unchanged()
    {
        CalendarTask[] tasks =
        [
            s_weekdays with { Start = 13 * 60, Duration = 120 },
            s_weekdays with { Start = 8 * 60, Duration = 240 },
        ];

        var editor = Create(tasks);

        Assert.Equal([TimeSpan.FromHours(8), TimeSpan.FromHours(13)], editor.Rows.Select(r => r.Start));
        Assert.Equal([TimeSpan.FromHours(12), TimeSpan.FromHours(15)], editor.Rows.Select(r => r.End));
        Assert.Equal(tasks.OrderBy(t => t.Start), editor.ToTasks());
        Assert.True(editor.IsValid);
        Assert.False(editor.HasError);
    }

    [Fact]
    public void Whole_day_slot_shows_00_00_to_00_00_and_comes_back_whole()
    {
        var editor = Create([s_everyDay with { Start = 0, Duration = 1440 }]);

        var row = Assert.Single(editor.Rows);
        Assert.Equal(TimeSpan.Zero, row.Start);
        Assert.Equal(TimeSpan.Zero, row.End);
        Assert.Equal(s_everyDay with { Start = 0, Duration = 1440 }, Assert.Single(editor.ToTasks()));
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void Slot_ending_at_midnight_shows_an_end_of_00_00_and_comes_back_unchanged()
    {
        var editor = Create([s_weekdays with { Start = 22 * 60, Duration = 120 }]);

        var row = Assert.Single(editor.Rows);
        Assert.Equal(TimeSpan.FromHours(22), row.Start);
        Assert.Equal(TimeSpan.Zero, row.End);
        Assert.Equal(s_weekdays with { Start = 22 * 60, Duration = 120 }, Assert.Single(editor.ToTasks()));
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void Editing_an_end_to_midnight_keeps_the_schedule_valid()
    {
        var editor = Create([s_weekdays with { Start = 20 * 60, Duration = 60 }]);

        editor.Rows[0].End = TimeSpan.Zero;

        Assert.True(editor.IsValid);
        Assert.Equal(240, Assert.Single(editor.ToTasks()).Duration);
    }

    [Fact]
    public void Slot_without_a_day_is_rejected()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 60 }]);
        var row = editor.Rows[0];

        row.Monday = row.Tuesday = row.Wednesday = row.Thursday = row.Friday = false;

        Assert.False(editor.IsValid);
        Assert.True(editor.HasError);
        Assert.Equal("Slot 1: select at least one day.", editor.ErrorText);
    }

    [Fact]
    public void Slot_ending_before_its_start_is_rejected()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 60 }, s_weekdays with { Start = 18 * 60, Duration = 60 }]);

        editor.Rows[1].End = TimeSpan.FromHours(17);

        Assert.False(editor.IsValid);
        Assert.Equal("Slot 2: the end must be after the start.", editor.ErrorText);
    }

    [Fact]
    public void Overlapping_slots_are_rejected_with_the_day()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 240 }, s_weekdays with { Start = 13 * 60, Duration = 60 }]);

        editor.Rows[1].Start = TimeSpan.FromHours(11);

        Assert.False(editor.IsValid);
        Assert.Equal("Slots 1 and 2 overlap on Monday.", editor.ErrorText);
    }

    [Fact]
    public void Fixing_the_issue_makes_the_schedule_valid_again()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 240 }, s_weekdays with { Start = 13 * 60, Duration = 60 }]);
        editor.Rows[1].Start = TimeSpan.FromHours(11);

        editor.Rows[1].Start = TimeSpan.FromHours(12);

        Assert.True(editor.IsValid);
        Assert.False(editor.HasError);
        Assert.Equal("", editor.ErrorText);
    }

    [Fact]
    public void New_slot_starts_after_the_last_one_on_weekdays()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 210 }]);

        editor.AddRowCommand.Execute(null);

        var added = editor.ToTasks()[1];
        Assert.Equal(s_weekdays with { Start = 12 * 60, Duration = 120 }, added);
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void First_slot_of_an_empty_schedule_starts_at_7()
    {
        var editor = Create([]);
        Assert.True(editor.IsEmpty);

        editor.AddRowCommand.Execute(null);

        Assert.False(editor.IsEmpty);
        Assert.Equal(s_weekdays with { Start = 7 * 60, Duration = 120 }, Assert.Single(editor.ToTasks()));
    }

    [Fact]
    public void Removing_a_slot_revalidates()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 240 }, s_weekdays with { Start = 10 * 60, Duration = 60 }]);
        Assert.False(editor.IsValid);

        editor.RemoveRow(editor.Rows[1]);
        Assert.True(editor.IsValid);

        editor.RemoveRow(editor.Rows[0]);
        Assert.True(editor.IsEmpty);
        Assert.Empty(editor.ToTasks());
    }

    [Fact]
    public void A_removed_slot_no_longer_affects_the_validation()
    {
        var editor = Create([s_weekdays with { Start = 8 * 60, Duration = 60 }, s_weekdays with { Start = 18 * 60, Duration = 60 }]);
        var removed = editor.Rows[1];
        editor.RemoveRow(removed);

        removed.End = TimeSpan.FromHours(1);

        Assert.True(editor.IsValid);
    }

    [Fact]
    public void Day_labels_follow_the_culture_from_Monday()
    {
        var row = Create([s_weekdays with { Start = 0, Duration = 60 }]).Rows[0];

        Assert.Equal(
            ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
            [row.MondayLabel, row.TuesdayLabel, row.WednesdayLabel, row.ThursdayLabel, row.FridayLabel, row.SaturdayLabel, row.SundayLabel]);
        Assert.Equal("Monday", row.MondayName);
        Assert.Equal("24HourClock", row.ClockIdentifier);
    }

    private static ScheduleEditorViewModel Create(IEnumerable<CalendarTask> tasks) => new(tasks, ReswStrings.English);
}
