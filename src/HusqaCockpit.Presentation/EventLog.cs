using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HusqaCockpit.Core.Fleet;

namespace HusqaCockpit.Presentation;

/// <summary>One thing that happened to a mower, as the app saw it: the data, not its wording (the page words it in the current language).</summary>
/// <param name="Time">When the app saw it.</param>
/// <param name="ErrorCode">For errors, alarms and recoveries.</param>
/// <param name="StartReason">For the start of a task.</param>
/// <param name="Duration">For the end of a task, when its start was seen.</param>
public sealed record EventEntry(
    DateTimeOffset Time,
    string MowerId,
    string MowerName,
    MowerAlertKind Kind,
    int ErrorCode = 0,
    TaskStartReason StartReason = TaskStartReason.Unknown,
    TimeSpan? Duration = null);

/// <summary>
/// The events the app sees on the mowers (errors and their end, stops, connection lost and back, start and end of
/// a mowing task), one JSON object per line, in one file per day under %LocalAppData%\HusqA Cockpit\Events. The
/// detail page of a mower shows its latest events. Like the message records of DySS Cockpit, the files are kept for
/// a chosen number of days (<see cref="KeepDays"/>), counted from the day in their name, which a copy or a restore of
/// the file does not change. Written from the thread that sees the change.
/// </summary>
public sealed class EventLog(string directory, int keepDays, TimeProvider? time = null)
{
    private const string Prefix = "events-";
    private const string Extension = ".jsonl";
    private const string DayFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };

    private readonly Lock _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private int _keepDays = keepDays;
    private DateOnly _purgedOn;

    public string Directory { get; } = directory;

    /// <summary>Raised after an event was written (on the thread that wrote it).</summary>
    public event EventHandler<EventEntry>? Recorded;

    /// <summary>How many days back the files are kept; zero or less keeps them all.</summary>
    public int KeepDays
    {
        get { lock (_gate) return _keepDays; }
        set
        {
            lock (_gate) _keepDays = value;
            Purge();
        }
    }

    /// <summary>Writes the alert, at the current time, to the file of the day. Failing to write is the caller's to handle (IOException).</summary>
    public EventEntry Record(MowerAlert alert)
    {
        var entry = new EventEntry(
            _time.GetUtcNow(), alert.Mower.Id, alert.Mower.Name, alert.Kind, alert.ErrorCode, alert.StartReason, alert.Duration);
        var line = JsonSerializer.Serialize(new Line(entry.Time, entry.MowerId, entry.MowerName, entry.Kind, entry.ErrorCode, entry.StartReason, entry.Duration?.TotalSeconds), s_json);
        var today = Today;
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            // No byte order mark: it breaks JSON parsers.
            File.AppendAllText(FileOf(DateOnly.FromDateTime(entry.Time.ToLocalTime().DateTime)), line + Environment.NewLine, new UTF8Encoding(false));
            if (_purgedOn != today)
            {
                DeleteOlder(_keepDays, today);
            }
        }
        Recorded?.Invoke(this, entry);
        return entry;
    }

    /// <summary>The latest events of a mower, the newest first.</summary>
    public IReadOnlyList<EventEntry> Recent(string mowerId, int count)
    {
        var result = new List<EventEntry>();
        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return result;
            }
            // The day is in the name, so the newest day sorts last: read from there until there are enough.
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, $"{Prefix}*{Extension}").OrderByDescending(f => f, StringComparer.Ordinal))
            {
                string[] lines;
                lock (_gate) lines = File.ReadAllLines(file, Encoding.UTF8);
                foreach (var line in lines.Reverse())
                {
                    if (Parse(line) is { } entry && entry.MowerId == mowerId)
                    {
                        result.Add(entry);
                        if (result.Count == count)
                        {
                            return result;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What could be read is shown.
        }
        return result;
    }

    /// <summary>How many files would go if the events were kept <paramref name="days"/> days: what the user is told before it is done.</summary>
    public int CountOlderThan(int days)
    {
        try { return OlderThan(days, Today).Count; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Deletes the files that are too old as of today (at start-up, and when the number of days changes).</summary>
    public void Purge()
    {
        lock (_gate) DeleteOlder(_keepDays, Today);
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    private string FileOf(DateOnly day) => Path.Combine(Directory, $"{Prefix}{day.ToString(DayFormat, CultureInfo.InvariantCulture)}{Extension}");

    private void DeleteOlder(int days, DateOnly today)
    {
        _purgedOn = today;
        try
        {
            foreach (var file in OlderThan(days, today))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // open elsewhere: next time
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again at the next start, or the next day.
        }
    }

    /// <summary>Files of another name are left alone; so are those from the last <paramref name="days"/> days (with 7, a week ago stays and eight days ago goes).</summary>
    private List<string> OlderThan(int days, DateOnly today)
    {
        if (days <= 0 || !System.IO.Directory.Exists(Directory))
        {
            return [];
        }
        return System.IO.Directory.EnumerateFiles(Directory, $"{Prefix}*{Extension}")
            .Where(file => DateOnly.TryParseExact(Path.GetFileName(file)[Prefix.Length..^Extension.Length], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < today.AddDays(-days))
            .ToList();
    }

    private static EventEntry? Parse(string line)
    {
        try
        {
            var l = JsonSerializer.Deserialize<Line>(line, s_json);
            return l is { MowerId: not null, MowerName: not null } && Enum.IsDefined(l.Kind)
                ? new EventEntry(l.Time, l.MowerId, l.MowerName, l.Kind, l.ErrorCode, l.StartReason, l.DurationSeconds is { } s ? TimeSpan.FromSeconds(s) : null)
                : null;
        }
        catch (JsonException)
        {
            return null;   // a line cut by a power failure, or written by a newer version: skipped
        }
    }

    private sealed record Line(
        DateTimeOffset Time,
        string? MowerId,
        string? MowerName,
        MowerAlertKind Kind,
        int ErrorCode,
        TaskStartReason StartReason,
        double? DurationSeconds);
}
