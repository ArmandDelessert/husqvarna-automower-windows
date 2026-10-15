using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Presentation.Tests.Support;

namespace HusqaCockpit.Presentation.Tests;

public sealed class EventFormatterTests : IDisposable
{
    private readonly IDisposable _culture = TestCulture.Use(TestCulture.English);
    private readonly EventFormatter _formatter = new(ReswStrings.English);

    public void Dispose() => _culture.Dispose();

    private static EventEntry Entry(MowerAlertKind kind, int code = 0, TaskStartReason reason = TaskStartReason.Unknown, TimeSpan? duration = null) =>
        new(TestMowers.Now.AddHours(-2), "mower-1", "Alpha", kind, code, reason, duration);

    private EventItem Describe(EventEntry entry) => _formatter.Describe(entry, TestMowers.Now);

    [Fact]
    public void An_error_says_which_one_in_red_with_when()
    {
        Assert.Equal(
            new EventItem("today at 10:00", "Error", "Outside working area", "\uE783", StatusSeverity.Error),
            Describe(Entry(MowerAlertKind.Error, code: 1)));
    }

    [Fact]
    public void A_recovery_names_the_error_that_went_away()
    {
        Assert.Equal(
            new EventItem("today at 10:00", "Error resolved", "Outside working area", "\uE73E", StatusSeverity.Ok),
            Describe(Entry(MowerAlertKind.ErrorCleared, code: 1)));
    }

    [Theory]
    [InlineData(TaskStartReason.Schedule, "Following its schedule.")]
    [InlineData(TaskStartReason.Application, "Started from HusqA Cockpit.")]
    [InlineData(TaskStartReason.Manual, "Started manually, outside its schedule.")]
    [InlineData(TaskStartReason.Unknown, "Mowing has started.")]
    public void The_start_of_a_task_says_why(TaskStartReason reason, string detail)
    {
        Assert.Equal(
            new EventItem("today at 10:00", "Mowing started", detail, "\uE8C6", StatusSeverity.Ok),
            Describe(Entry(MowerAlertKind.TaskStarted, reason: reason)));
    }

    [Fact]
    public void The_end_of_a_task_says_how_long_it_lasted_when_known()
    {
        Assert.Equal("Duration: 1 h 30, recharges included.", Describe(Entry(MowerAlertKind.TaskFinished, duration: TimeSpan.FromMinutes(90))).Detail);
        Assert.Equal("Mowing is over.", Describe(Entry(MowerAlertKind.TaskFinished)).Detail);
    }

    [Theory]
    [InlineData(MowerAlertKind.Alarm, "Theft alarm", StatusSeverity.Error)]
    [InlineData(MowerAlertKind.Stopped, "Stopped", StatusSeverity.Warning)]
    [InlineData(MowerAlertKind.Disconnected, "Disconnected", StatusSeverity.Offline)]
    [InlineData(MowerAlertKind.Reconnected, "Reconnected", StatusSeverity.Ok)]
    [InlineData(MowerAlertKind.TaskFinished, "Mowing finished", StatusSeverity.Info)]
    public void Every_kind_has_a_title_and_a_severity(MowerAlertKind kind, string title, StatusSeverity severity)
    {
        var item = Describe(Entry(kind, code: 71));

        Assert.Equal((title, severity), (item.Title, item.Severity));
        Assert.NotEmpty(item.Glyph);
    }

    [Fact]
    public void The_words_follow_the_language()
    {
        using var culture = TestCulture.Use(TestCulture.French);

        var item = new EventFormatter(ReswStrings.French).Describe(Entry(MowerAlertKind.TaskStarted, reason: TaskStartReason.Schedule), TestMowers.Now);

        Assert.Equal(("aujourd'hui à 10:00", "Début de tonte", "Selon son planning."), (item.When, item.Title, item.Detail));
    }
}
