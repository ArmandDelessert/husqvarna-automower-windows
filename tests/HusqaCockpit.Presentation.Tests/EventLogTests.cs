using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace HusqaCockpit.Presentation.Tests;

public sealed class EventLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "husqa-events-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(TestMowers.Now.ToUniversalTime());

    public EventLogTests() => _time.SetLocalTimeZone(TestMowers.Zone);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private EventLog Log(int keepDays = 30) => new(_folder, keepDays, _time);

    private static MowerAlert Alert(MowerAlertKind kind, string id = "mower-1", string name = "Alpha", int code = 0,
        TaskStartReason reason = TaskStartReason.Unknown, TimeSpan? duration = null) =>
        new(kind, TestMowers.Create(TestMowers.Parked(name), id), code, reason, duration);

    private void WriteOldFile(int daysAgo)
    {
        Directory.CreateDirectory(_folder);
        var day = DateOnly.FromDateTime(_time.GetLocalNow().DateTime).AddDays(-daysAgo);
        File.WriteAllText(Path.Combine(_folder, $"events-{day:yyyy-MM-dd}.jsonl"), "");
    }

    private string[] Files() => Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)).Order().ToArray();

    [Fact]
    public void An_event_is_written_to_the_file_of_the_day_and_read_back_whole()
    {
        var log = Log();

        log.Record(Alert(MowerAlertKind.TaskFinished, duration: TimeSpan.FromMinutes(90)));
        log.Record(Alert(MowerAlertKind.TaskStarted, reason: TaskStartReason.Application));
        log.Record(Alert(MowerAlertKind.Error, code: 1));

        Assert.Equal(["events-2026-06-10.jsonl"], Files());
        var events = log.Recent("mower-1", 10);
        Assert.Equal([MowerAlertKind.Error, MowerAlertKind.TaskStarted, MowerAlertKind.TaskFinished], events.Select(e => e.Kind));
        Assert.Equal(1, events[0].ErrorCode);
        Assert.Equal(TaskStartReason.Application, events[1].StartReason);
        Assert.Equal(TimeSpan.FromMinutes(90), events[2].Duration);
        Assert.All(events, e => Assert.Equal(("mower-1", "Alpha", _time.GetUtcNow()), (e.MowerId, e.MowerName, e.Time)));
    }

    [Fact]
    public void The_file_is_json_lines_in_camel_case_with_the_kind_spelled_out_and_no_byte_order_mark()
    {
        Log().Record(Alert(MowerAlertKind.TaskStarted, reason: TaskStartReason.Schedule));

        var path = Path.Combine(_folder, "events-2026-06-10.jsonl");
        var line = Assert.Single(File.ReadAllLines(path));
        Assert.Contains("\"kind\":\"TaskStarted\"", line, StringComparison.Ordinal);
        Assert.Contains("\"startReason\":\"Schedule\"", line, StringComparison.Ordinal);
        Assert.Contains("\"mowerId\":\"mower-1\"", line, StringComparison.Ordinal);
        Assert.Equal((byte)'{', File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void Events_of_other_mowers_are_not_returned_and_the_count_is_respected()
    {
        var log = Log();
        for (var i = 0; i < 5; i++)
        {
            log.Record(Alert(MowerAlertKind.Disconnected));
        }
        log.Record(Alert(MowerAlertKind.Error, id: "mower-2", name: "Bravo", code: 3));

        Assert.Equal(3, log.Recent("mower-1", 3).Count);
        Assert.Equal(5, log.Recent("mower-1", 50).Count);
        Assert.Equal(3, Assert.Single(log.Recent("mower-2", 50)).ErrorCode);
        Assert.Empty(log.Recent("mower-3", 50));
    }

    [Fact]
    public void Reading_goes_from_the_newest_day_back_and_across_days()
    {
        var log = Log();
        log.Record(Alert(MowerAlertKind.Error, code: 1));
        _time.Advance(TimeSpan.FromDays(1));
        log.Record(Alert(MowerAlertKind.Error, code: 2));
        _time.Advance(TimeSpan.FromDays(1));
        log.Record(Alert(MowerAlertKind.Error, code: 3));

        Assert.Equal([3, 2, 1], log.Recent("mower-1", 10).Select(e => e.ErrorCode));
        Assert.Equal([3, 2], log.Recent("mower-1", 2).Select(e => e.ErrorCode));
    }

    [Fact]
    public void A_broken_or_unknown_line_is_skipped_and_the_others_are_kept()
    {
        var log = Log();
        log.Record(Alert(MowerAlertKind.Error, code: 1));
        File.AppendAllText(
            Path.Combine(_folder, "events-2026-06-10.jsonl"),
            "{\"time\":\"2026-06-1\n{\"time\":\"2026-06-10T10:00:00+00:00\",\"mowerId\":\"mower-1\",\"mowerName\":\"A\",\"kind\":\"FromTheFuture\"}\n");
        log.Record(Alert(MowerAlertKind.Error, code: 2));

        Assert.Equal([2, 1], log.Recent("mower-1", 10).Select(e => e.ErrorCode));
    }

    [Fact]
    public void Nothing_is_read_before_anything_is_written()
    {
        Assert.Empty(Log().Recent("mower-1", 10));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void Recording_raises_the_event_with_what_was_written()
    {
        var log = Log();
        EventEntry? seen = null;
        log.Recorded += (_, e) => seen = e;

        var written = log.Record(Alert(MowerAlertKind.Reconnected));

        Assert.Same(written, seen);
    }

    [Fact]
    public void Files_older_than_the_retention_are_deleted_by_the_day_in_their_name_and_nothing_else_is_touched()
    {
        WriteOldFile(daysAgo: 7);
        WriteOldFile(daysAgo: 8);
        WriteOldFile(daysAgo: 400);
        File.WriteAllText(Path.Combine(_folder, "events-notes.txt"), "mine");
        File.WriteAllText(Path.Combine(_folder, "other-2020-01-01.jsonl"), "mine");

        Log(keepDays: 7).Purge();

        Assert.Equal(["events-2026-06-03.jsonl", "events-notes.txt", "other-2020-01-01.jsonl"], Files());
    }

    [Fact]
    public void A_retention_of_zero_keeps_everything()
    {
        WriteOldFile(daysAgo: 4000);

        var log = Log(keepDays: 0);
        log.Purge();

        Assert.Single(Files());
        Assert.Equal(0, log.CountOlderThan(0));
    }

    [Fact]
    public void Changing_the_retention_deletes_what_is_now_too_old_at_once_and_the_count_tells_how_many()
    {
        WriteOldFile(daysAgo: 5);
        WriteOldFile(daysAgo: 20);
        var log = Log(keepDays: 30);

        Assert.Equal(2, log.CountOlderThan(3));
        Assert.Equal(1, log.CountOlderThan(10));
        Assert.Equal(0, log.CountOlderThan(30));

        log.KeepDays = 10;

        Assert.Equal(10, log.KeepDays);
        Assert.Single(Files());
    }

    [Fact]
    public void Writing_purges_once_a_day()
    {
        var log = Log(keepDays: 7);
        log.Record(Alert(MowerAlertKind.Error));
        WriteOldFile(daysAgo: 30);

        log.Record(Alert(MowerAlertKind.Error));
        Assert.Equal(2, Files().Length);   // same day: not purged again

        _time.Advance(TimeSpan.FromDays(1));
        log.Record(Alert(MowerAlertKind.Error));
        Assert.DoesNotContain(Files(), f => f.Contains("2026-05", StringComparison.Ordinal));
    }
}
