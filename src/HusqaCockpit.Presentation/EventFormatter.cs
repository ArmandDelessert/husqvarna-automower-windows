using HusqaCockpit.Core.Fleet;

namespace HusqaCockpit.Presentation;

/// <summary>An event as the detail page of a mower shows it.</summary>
/// <param name="When">"today at 10:30".</param>
/// <param name="Title">"Mowing started", "Error", "Disconnected"…</param>
/// <param name="Detail">The error, the reason a task started, how long it lasted; may be empty.</param>
/// <param name="Glyph">A Segoe Fluent Icons glyph.</param>
/// <param name="Severity">The color of the glyph, as for a status.</param>
public sealed record EventItem(string When, string Title, string Detail, string Glyph, StatusSeverity Severity);

/// <summary>Words the events of the <see cref="EventLog"/> in the language of the user.</summary>
public sealed class EventFormatter(IStrings strings)
{
    private readonly MowerFormatter _formatter = new(strings);

    public EventItem Describe(EventEntry entry, DateTimeOffset now)
    {
        var (title, detail, glyph, severity) = entry.Kind switch
        {
            MowerAlertKind.Error => (strings.Text("History_Error"), strings.ErrorCode(entry.ErrorCode), "", StatusSeverity.Error),
            MowerAlertKind.Alarm => (strings.Text("History_Alarm"), strings.ErrorCode(entry.ErrorCode), "", StatusSeverity.Error),
            MowerAlertKind.ErrorCleared => (strings.Text("History_ErrorCleared"), strings.ErrorCode(entry.ErrorCode), "", StatusSeverity.Ok),
            MowerAlertKind.Stopped => (strings.Text("History_Stopped"), strings.Text("Status_StoppedDetail"), "", StatusSeverity.Warning),
            MowerAlertKind.Disconnected => (strings.Text("Status_Disconnected"), "", "", StatusSeverity.Offline),
            MowerAlertKind.Reconnected => (strings.Text("History_Reconnected"), "", "", StatusSeverity.Ok),
            MowerAlertKind.TaskStarted => (strings.Text("History_TaskStarted"), StartedDetail(entry.StartReason), "", StatusSeverity.Ok),
            MowerAlertKind.TaskFinished => (strings.Text("History_TaskFinished"), FinishedDetail(entry.Duration), "", StatusSeverity.Info),
            _ => (entry.Kind.ToString(), "", "", StatusSeverity.Info),
        };
        return new EventItem(_formatter.When(entry.Time, now), title, detail, glyph, severity);
    }

    private string StartedDetail(TaskStartReason reason) => strings.Text(reason switch
    {
        TaskStartReason.Schedule => "Notification_TaskStartedSchedule",
        TaskStartReason.Application => "Notification_TaskStartedApplication",
        TaskStartReason.Manual => "Notification_TaskStartedManual",
        _ => "Notification_TaskStartedUnknown",
    });

    private string FinishedDetail(TimeSpan? duration) => duration is { } elapsed
        ? strings.Format("Notification_TaskFinishedDuration", _formatter.Duration(elapsed))
        : strings.Text("Notification_TaskFinishedUnknown");
}
