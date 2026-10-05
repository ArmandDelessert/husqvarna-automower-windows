using Microsoft.Extensions.Time.Testing;

namespace HusqaCockpit.Core.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also records the timers the code under test starts (Task.Delay,
/// timed cancellations), so that a test can wait until the code is waiting before it advances the clock:
/// advancing earlier would make the timer start from the new time.
/// </summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private static readonly TimeSpan s_realTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _fake = new(start);
    private readonly List<TimeSpan> _startedTimers = [];
    private readonly List<TimeSpan> _allTimers = [];

    public override TimeZoneInfo LocalTimeZone => _fake.LocalTimeZone;

    public override long TimestampFrequency => _fake.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => _fake.GetUtcNow();

    public override long GetTimestamp() => _fake.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _fake.CreateTimer(callback, state, dueTime, period);
        lock (_startedTimers)
        {
            _startedTimers.Add(dueTime);
            _allTimers.Add(dueTime);
        }
        return timer;
    }

    /// <summary>How many timers of <paramref name="dueTime"/> have been started since the beginning.</summary>
    public int StartedTimers(TimeSpan dueTime)
    {
        lock (_startedTimers)
        {
            return _allTimers.Count(t => t == dueTime);
        }
    }

    public void Advance(TimeSpan delta) => _fake.Advance(delta);

    /// <summary>Waits until a timer of <paramref name="dueTime"/> has been started, then advances the clock by it.</summary>
    public async Task AdvanceWhenWaitingAsync(TimeSpan dueTime)
    {
        await WaitForTimerAsync(dueTime);
        Advance(dueTime);
    }

    /// <summary>Waits (in real time) until a timer of <paramref name="dueTime"/> has been started; each timer counts once.</summary>
    public async Task WaitForTimerAsync(TimeSpan dueTime)
    {
        var deadline = DateTime.UtcNow + s_realTimeout;
        while (true)
        {
            lock (_startedTimers)
            {
                var index = _startedTimers.IndexOf(dueTime);
                if (index >= 0)
                {
                    _startedTimers.RemoveAt(index);
                    return;
                }
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No timer of {dueTime} was started.");
            }
            await Task.Delay(5);
        }
    }

    /// <summary>Waits (in real time) until <paramref name="condition"/> holds.</summary>
    public static async Task Until(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + s_realTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {description}.");
            }
            await Task.Delay(5);
        }
    }
}
