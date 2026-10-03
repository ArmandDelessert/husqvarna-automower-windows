using System.Globalization;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.App.Services;

public enum StatusSeverity
{
    Ok,
    Info,
    Warning,
    Error,
    Offline,
}

public sealed record StatusSummary(string Title, string Detail, StatusSeverity Severity);

/// <summary>Turns the raw API state into the short sentences shown on the dashboard.</summary>
public static class MowerFormatter
{
    public static StatusSummary Summarize(Mower mower, DateTimeOffset now)
    {
        if (!mower.IsConnected)
        {
            var lastSeen = mower.LastStatusTime is { } seen ? Loc.Format("Status_LastSeen", Relative(seen, now)) : "";
            return new StatusSummary(Loc.Get("Status_Disconnected"), lastSeen, StatusSeverity.Offline);
        }

        if (mower.HasError)
        {
            var title = mower.ErrorCode != 0 ? Loc.ErrorCode(mower.ErrorCode) : Loc.Enum(mower.State);
            var detail = mower.ErrorTime is { } since ? Loc.Format("Status_ErrorSince", When(since, now)) : "";
            return new StatusSummary(title, detail, StatusSeverity.Error);
        }

        switch (mower.State)
        {
            case MowerState.Off:
            case MowerState.WaitUpdating:
            case MowerState.WaitPowerUp:
                return new StatusSummary(Loc.Enum(mower.State), "", StatusSeverity.Info);
            case MowerState.Stopped:
                return new StatusSummary(Loc.Get("Status_Stopped"), Loc.Get("Status_StoppedDetail"), StatusSeverity.Warning);
            case MowerState.Paused:
                return new StatusSummary(Loc.Get("Status_Paused"), Loc.Get("Status_PausedDetail"), StatusSeverity.Warning);
        }

        var planner = mower.Attributes.Planner;
        var nextStart = mower.NextStart is { } next ? Loc.Format("Status_NextStart", When(next, now)) : "";

        switch (mower.Activity)
        {
            case MowerActivity.Mowing:
                var mowingDetail = planner.Override.Action == OverrideAction.ForceMow
                    ? Loc.Get("Status_MowingOverride")
                    : Loc.Get("Status_MowingSchedule");
                if (mower.Attributes.Mower.InactiveReason == InactiveReason.SearchingForSatellites)
                {
                    mowingDetail = Loc.Get("InactiveReason_SearchingForSatellites");
                }
                return new StatusSummary(Loc.Get("Status_Mowing"), mowingDetail, StatusSeverity.Ok);

            case MowerActivity.Charging:
                var remaining = mower.Attributes.Battery.RemainingChargingTime;
                var chargingDetail = remaining > 0
                    ? Loc.Format("Status_ChargingRemaining", Duration(TimeSpan.FromSeconds(remaining)))
                    : nextStart;
                return new StatusSummary(Loc.Get("Status_Charging"), chargingDetail, StatusSeverity.Ok);

            case MowerActivity.ParkedInCs:
                return Parked(mower, planner, nextStart, now);

            case MowerActivity.GoingHome:
            case MowerActivity.Leaving:
            case MowerActivity.StoppedInGarden:
                return new StatusSummary(Loc.Enum(mower.Activity), nextStart, StatusSeverity.Ok);

            default:
                return new StatusSummary(Loc.Enum(mower.State), nextStart, StatusSeverity.Info);
        }
    }

    private static StatusSummary Parked(Mower mower, PlannerInfo planner, string nextStart, DateTimeOffset now)
    {
        switch (planner.RestrictedReason)
        {
            case RestrictedReason.ParkOverride when mower.NextStart is null:
                return new StatusSummary(Loc.Get("Status_ParkedUntilFurtherNotice"), Loc.Get("Status_ParkedUntilFurtherNoticeDetail"), StatusSeverity.Info);
            case RestrictedReason.ParkOverride:
                return new StatusSummary(Loc.Get("Status_Parked"), Loc.Format("Status_ParkedUntil", When(mower.NextStart!.Value, now)), StatusSeverity.Info);
            case RestrictedReason.None:
            case RestrictedReason.NotApplicable:
            case RestrictedReason.Unknown:
            case RestrictedReason.WeekSchedule:
                return new StatusSummary(Loc.Get("Status_Parked"), nextStart.Length > 0 ? nextStart : Loc.Get("Status_NoSchedule"), StatusSeverity.Ok);
            default:
                var reason = Loc.Enum(planner.RestrictedReason);
                return new StatusSummary(Loc.Get("Status_Parked"), nextStart.Length > 0 ? $"{reason} · {nextStart}" : reason, StatusSeverity.Info);
        }
    }

    /// <summary>"today at 08:00", "tomorrow at 08:00", "Mon 5 Oct at 08:00".</summary>
    public static string When(DateTimeOffset moment, DateTimeOffset now)
    {
        var local = moment.ToLocalTime();
        var days = (local.Date - now.ToLocalTime().Date).Days;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        return days switch
        {
            0 => Loc.Format("When_Today", time),
            1 => Loc.Format("When_Tomorrow", time),
            -1 => Loc.Format("When_Yesterday", time),
            _ => Loc.Format("When_Date", local.ToString(Loc.Get("When_DatePattern"), CultureInfo.CurrentCulture), time),
        };
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "12 days ago".</summary>
    public static string Relative(DateTimeOffset moment, DateTimeOffset now)
    {
        var elapsed = now - moment;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => Loc.Get("Relative_JustNow"),
            { TotalHours: < 1 } => Loc.Format("Relative_Minutes", (int)elapsed.TotalMinutes),
            { TotalDays: < 1 } => Loc.Format("Relative_Hours", (int)elapsed.TotalHours),
            _ => Loc.Format("Relative_Days", (int)elapsed.TotalDays),
        };
    }

    /// <summary>"45 min", "2 h 30".</summary>
    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? Loc.Format("Duration_HoursMinutes", (int)duration.TotalHours, duration.Minutes)
            : Loc.Format("Duration_Minutes", Math.Max(1, (int)Math.Round(duration.TotalMinutes)));

    /// <summary>"Mon, Tue, Thu" for a schedule task.</summary>
    public static string Days(CalendarTask task)
    {
        var days = task.Days.ToList();
        if (days.Count == 7)
        {
            return Loc.Get("Schedule_EveryDay");
        }
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        return string.Join(", ", days.Select(d => names[(int)d]));
    }

    public static string TimeRange(CalendarTask task)
    {
        var start = DateTime.Today.AddMinutes(task.Start);
        var end = start.AddMinutes(task.Duration);
        return $"{start.ToString("t", CultureInfo.CurrentCulture)} – {end.ToString("t", CultureInfo.CurrentCulture)}";
    }

    public static string Hours(long? seconds) =>
        seconds is { } s ? Loc.Format("Unit_Hours", (s / 3600.0).ToString("N0", CultureInfo.CurrentCulture)) : "—";

    public static string Kilometers(long? meters) =>
        meters is { } m ? Loc.Format("Unit_Kilometers", (m / 1000.0).ToString("N0", CultureInfo.CurrentCulture)) : "—";

    public static string Count(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
}
