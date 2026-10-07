using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Presentation.Tests.Support;

namespace HusqaCockpit.Presentation.Tests;

public sealed class AlertMessagesTests
{
    private static readonly Core.Models.Mower s_mower = TestMowers.Create(TestMowers.Mowing("Alpha"));

    private static MowerAlert Alert(MowerAlertKind kind, TaskStartReason reason = TaskStartReason.Unknown, TimeSpan? duration = null) =>
        new(kind, s_mower, ErrorCode: 9, reason, duration);

    // ----- Which alerts become notifications -----

    [Theory]
    [InlineData(TaskNotificationMode.None, false, false)]
    [InlineData(TaskNotificationMode.EndOnly, false, true)]
    [InlineData(TaskNotificationMode.StartAndEnd, true, true)]
    public void The_task_setting_decides_about_the_start_and_the_end(TaskNotificationMode mode, bool start, bool end)
    {
        var settings = new AppSettings { TaskNotifications = mode };

        Assert.Equal(start, AlertMessages.IsEnabled(MowerAlertKind.TaskStarted, settings));
        Assert.Equal(end, AlertMessages.IsEnabled(MowerAlertKind.TaskFinished, settings));
    }

    [Fact]
    public void Task_notifications_are_off_by_default()
    {
        var settings = new AppSettings();

        Assert.Equal(TaskNotificationMode.None, settings.TaskNotifications);
        Assert.False(AlertMessages.IsEnabled(MowerAlertKind.TaskStarted, settings));
        Assert.False(AlertMessages.IsEnabled(MowerAlertKind.TaskFinished, settings));
    }

    [Fact]
    public void The_task_setting_does_not_touch_the_other_notifications()
    {
        var everything = new AppSettings { TaskNotifications = TaskNotificationMode.StartAndEnd };
        var nothing = new AppSettings
        {
            NotifyErrors = false,
            NotifyRecoveries = false,
            NotifyStopped = false,
            NotifyConnectivity = false,
            TaskNotifications = TaskNotificationMode.StartAndEnd,
        };

        foreach (var kind in new[]
        {
            MowerAlertKind.Error, MowerAlertKind.Alarm, MowerAlertKind.ErrorCleared,
            MowerAlertKind.Stopped, MowerAlertKind.Disconnected, MowerAlertKind.Reconnected,
        })
        {
            Assert.True(AlertMessages.IsEnabled(kind, everything), kind.ToString());
            Assert.False(AlertMessages.IsEnabled(kind, nothing), kind.ToString());
        }
    }

    [Fact]
    public void Each_existing_switch_still_decides_about_its_alerts()
    {
        var settings = new AppSettings { NotifyErrors = false, NotifyStopped = false };

        Assert.False(AlertMessages.IsEnabled(MowerAlertKind.Error, settings));
        Assert.False(AlertMessages.IsEnabled(MowerAlertKind.Alarm, settings));
        Assert.False(AlertMessages.IsEnabled(MowerAlertKind.Stopped, settings));
        Assert.True(AlertMessages.IsEnabled(MowerAlertKind.ErrorCleared, settings));
        Assert.True(AlertMessages.IsEnabled(MowerAlertKind.Disconnected, settings));
        Assert.True(AlertMessages.IsEnabled(MowerAlertKind.Reconnected, settings));
    }

    // ----- What they say -----

    [Theory]
    [InlineData(TaskStartReason.Schedule, "Following its schedule.")]
    [InlineData(TaskStartReason.Application, "Started from HusqA Cockpit.")]
    [InlineData(TaskStartReason.Manual, "Started manually, outside its schedule.")]
    [InlineData(TaskStartReason.Unknown, "Mowing has started.")]
    public void A_start_says_the_name_and_why_in_English(TaskStartReason reason, string body)
    {
        using var _ = TestCulture.Use(TestCulture.English);

        var (title, text) = new AlertMessages(ReswStrings.English).Describe(Alert(MowerAlertKind.TaskStarted, reason));

        Assert.Equal("Alpha starts mowing", title);
        Assert.Equal(body, text);
    }

    [Theory]
    [InlineData(TaskStartReason.Schedule, "Selon son planning.")]
    [InlineData(TaskStartReason.Application, "Lancée depuis HusqA Cockpit.")]
    [InlineData(TaskStartReason.Manual, "Démarrage manuel, hors planning.")]
    [InlineData(TaskStartReason.Unknown, "La tonte a démarré.")]
    public void A_start_says_the_name_and_why_in_French(TaskStartReason reason, string body)
    {
        using var _ = TestCulture.Use(TestCulture.French);

        var (title, text) = new AlertMessages(ReswStrings.French).Describe(Alert(MowerAlertKind.TaskStarted, reason));

        Assert.Equal("Alpha commence à tondre", title);
        Assert.Equal(body, text);
    }

    [Fact]
    public void An_end_says_the_name_and_the_duration()
    {
        using var english = TestCulture.Use(TestCulture.English);
        var finished = Alert(MowerAlertKind.TaskFinished, duration: TimeSpan.FromMinutes(125));

        var (title, text) = new AlertMessages(ReswStrings.English).Describe(finished);

        Assert.Equal("Alpha has finished mowing", title);
        Assert.Equal("Duration: 2 h 05, recharges included.", text);

        using var french = TestCulture.Use(TestCulture.French);
        var (frenchTitle, frenchText) = new AlertMessages(ReswStrings.French).Describe(finished);

        Assert.Equal("Alpha a terminé sa tonte", frenchTitle);
        Assert.Equal("Durée : 2 h 05, recharges comprises.", frenchText);
    }

    [Fact]
    public void An_end_without_a_known_duration_just_says_it_is_over()
    {
        using var _ = TestCulture.Use(TestCulture.English);

        var (_, text) = new AlertMessages(ReswStrings.English).Describe(Alert(MowerAlertKind.TaskFinished));

        Assert.Equal("Mowing is over.", text);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Every_alert_has_a_title_and_a_text_in_both_languages(string language)
    {
        var french = language == "fr-FR";
        using var _ = TestCulture.Use(french ? TestCulture.French : TestCulture.English);
        var messages = new AlertMessages(french ? ReswStrings.French : ReswStrings.English);

        foreach (var kind in Enum.GetValues<MowerAlertKind>())
        {
            var (title, text) = messages.Describe(Alert(kind, TaskStartReason.Schedule, TimeSpan.FromMinutes(90)));

            Assert.False(string.IsNullOrWhiteSpace(title), $"{kind} has no title");
            Assert.False(string.IsNullOrWhiteSpace(text), $"{kind} has no text");
            // A missing text is shown as its key.
            Assert.DoesNotContain("Notification_", title + text, StringComparison.Ordinal);
            Assert.Contains("Alpha", title, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_unknown_kind_of_alert_is_a_programming_error()
    {
        var messages = new AlertMessages(ReswStrings.English);

        Assert.Throws<ArgumentOutOfRangeException>(() => messages.Describe(Alert((MowerAlertKind)999)));
    }
}
