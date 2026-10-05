using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using HusqaCockpit.Core.Api;

namespace HusqaCockpit.Core.Tests;

/// <summary>A WebSocket whose frames are pushed by the test; it can also refuse the connection with an HTTP status.</summary>
internal sealed class FakeWebSocket(HttpStatusCode refusal = 0) : IWebSocketConnection
{
    private readonly Channel<(byte[] Data, WebSocketMessageType Type, bool EndOfMessage)> _incoming =
        Channel.CreateUnbounded<(byte[], WebSocketMessageType, bool)>();

    private int _keepAlives;

    public WebSocketState State { get; private set; } = WebSocketState.None;

    public HttpStatusCode HttpStatusCode { get; private set; }

    public string? Token { get; private set; }

    public int KeepAlivesSent => Volatile.Read(ref _keepAlives);

    public bool ClosedByClient { get; private set; }

    public bool Disposed { get; private set; }

    public Task ConnectAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
    {
        Token = accessToken;
        if (refusal != 0)
        {
            // What ClientWebSocket does when the server answers the upgrade request with an error.
            HttpStatusCode = refusal;
            State = WebSocketState.Closed;
            throw new WebSocketException($"The server returned status code '{(int)refusal}' when status code '101' was expected.");
        }
        HttpStatusCode = HttpStatusCode.SwitchingProtocols;
        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var (data, type, endOfMessage) = await _incoming.Reader.ReadAsync(cancellationToken);
        if (type == WebSocketMessageType.Close)
        {
            State = WebSocketState.CloseReceived;
            return new WebSocketReceiveResult(0, type, true, WebSocketCloseStatus.NormalClosure, "Connection closed");
        }
        data.CopyTo(buffer.AsSpan());
        return new WebSocketReceiveResult(data.Length, type, endOfMessage);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        // The feed only sends keep-alives: empty text messages.
        Assert.True(buffer.IsEmpty && messageType == WebSocketMessageType.Text && endOfMessage);
        Interlocked.Increment(ref _keepAlives);
        return ValueTask.CompletedTask;
    }

    public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        ClosedByClient = true;
        State = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disposed = true;
        State = WebSocketState.Closed;
    }

    public void Receive(string text, bool endOfMessage = true) =>
        _incoming.Writer.TryWrite((Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage));

    public void CloseFromServer() => _incoming.Writer.TryWrite(([], WebSocketMessageType.Close, true));
}

public sealed class AutomowerEventFeedTests : IAsyncDisposable
{
    private const string Ready = """{"ready":true,"connectionId":"abc"}""";
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_receiveTimeout = TimeSpan.FromMinutes(5);

    private readonly TestClock _clock = new(s_t0);
    private readonly FakeTokens _tokens = new();
    private readonly Queue<FakeWebSocket> _plannedSockets = new();
    private readonly List<FakeWebSocket> _sockets = [];
    private readonly List<EventStreamStatus> _statuses = [];
    private readonly List<AutomowerEvent> _events = [];
    private readonly CancellationTokenSource _stop = new();
    private int _statusCursor;
    private Task _run = Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run;
        _stop.Dispose();
    }

    // ----- Failed connections -----

    [Fact]
    public async Task Failed_attempts_are_spaced_more_and_more()
    {
        TimeSpan[] delays =
        [
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
        ];
        Start(refuseAllWith: HttpStatusCode.BadGateway);

        var now = s_t0;
        foreach (var delay in delays)
        {
            var status = await NextStatusAsync(EventStreamState.Disconnected);
            Assert.Equal(now + delay, status.RetryAt);
            Assert.Contains("502", status.Detail, StringComparison.Ordinal);
            await _clock.AdvanceWhenWaitingAsync(delay);
            now += delay;
        }

        await NextStatusAsync(EventStreamState.Disconnected);
        Assert.Equal(delays.Length + 1, SocketCount);
        Assert.Equal(0, _tokens.Invalidations);
    }

    [Fact]
    public async Task Unauthorized_invalidates_the_token_before_retrying()
    {
        Plan(new FakeWebSocket(HttpStatusCode.Unauthorized));
        Start();

        var status = await NextStatusAsync(EventStreamState.Disconnected);
        Assert.Equal(1, _tokens.Invalidations);
        Assert.Equal(s_t0 + TimeSpan.FromSeconds(5), status.RetryAt);

        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromSeconds(5));
        var socket = await SocketAsync(2);
        socket.Receive(Ready);

        var connected = await NextStatusAsync(EventStreamState.Connected);
        Assert.Equal("token-1", socket.Token);
        Assert.True(connected.AfterOutage);
    }

    [Fact]
    public async Task Forbidden_is_retried_once_with_a_new_token_then_waits_30_minutes()
    {
        Plan(new FakeWebSocket(HttpStatusCode.Forbidden), new FakeWebSocket(HttpStatusCode.Forbidden));
        Start();

        // First refusal: a token issued before the API was connected lacks the scope; retry with a new one.
        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromSeconds(5));
        var forbidden = await NextStatusAsync(EventStreamState.Forbidden);
        Assert.Equal("token-1", (await SocketAsync(2)).Token);
        Assert.Equal(1, _tokens.Invalidations);
        Assert.Equal(s_t0 + TimeSpan.FromSeconds(5) + TimeSpan.FromMinutes(30), forbidden.RetryAt);
        Assert.DoesNotContain(_statuses, s => s.State == EventStreamState.Disconnected);

        // Second refusal: a configuration problem, retried every 30 minutes without new tokens.
        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromMinutes(30));
        var socket = await SocketAsync(3);
        socket.Receive(Ready);

        var connected = await NextStatusAsync(EventStreamState.Connected);
        Assert.True(connected.AfterOutage);
        Assert.Equal(1, _tokens.Invalidations);
    }

    [Fact]
    public async Task Connection_closed_before_any_message_counts_as_a_failure()
    {
        Start();
        (await SocketAsync(1)).CloseFromServer();

        var status = await NextStatusAsync(EventStreamState.Disconnected);
        Assert.Equal(s_t0 + TimeSpan.FromSeconds(5), status.RetryAt);

        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromSeconds(5));
        (await SocketAsync(2)).Receive(Ready);
        Assert.True((await NextStatusAsync(EventStreamState.Connected)).AfterOutage);
    }

    // ----- Open connections -----

    [Fact]
    public async Task Events_are_delivered_even_when_split_into_several_frames()
    {
        Start();
        var socket = await SocketAsync(1);

        socket.Receive(Ready);
        socket.Receive("""{"id":"mower-1","type":"battery-event-v2",""", endOfMessage: false);
        socket.Receive("""  "attributes":{"battery":{"batteryPercent":50}}}""");

        var connected = await NextStatusAsync(EventStreamState.Connected);
        Assert.False(connected.AfterOutage);
        await TestClock.Until(() => EventCount == 1, "the event is delivered");
        var evt = _events[0];
        Assert.Equal(("battery-event-v2", "mower-1"), (evt.Type, evt.MowerId));
        Assert.Equal(50, evt.Attributes["battery"]!["batteryPercent"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_keep_alive_is_sent_every_minute()
    {
        Start();
        var socket = await SocketAsync(1);
        socket.Receive(Ready);
        await NextStatusAsync(EventStreamState.Connected);

        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromMinutes(1));
        await TestClock.Until(() => socket.KeepAlivesSent == 1, "the first keep-alive");

        await _clock.AdvanceWhenWaitingAsync(TimeSpan.FromMinutes(1));
        await TestClock.Until(() => socket.KeepAlivesSent == 2, "the second keep-alive");
    }

    [Fact]
    public async Task Silent_connection_is_replaced_after_the_receive_timeout()
    {
        Start();
        var first = await SocketAsync(1);
        await _clock.WaitForTimerAsync(s_receiveTimeout);
        first.Receive(Ready);
        await NextStatusAsync(EventStreamState.Connected);

        await _clock.AdvanceWhenWaitingAsync(s_receiveTimeout);

        var second = await SocketAsync(2);
        Assert.True(first.ClosedByClient);
        Assert.True(first.Disposed);
        second.Receive(Ready);
        await NextStatusAsync(EventStreamState.Connected);
        Assert.DoesNotContain(_statuses, s => s.State == EventStreamState.Disconnected);
    }

    [Fact]
    public async Task Connection_is_renewed_before_the_two_hour_limit()
    {
        // Each receive starts a timer of the receive timeout: counting them tells when the feed waits for a message.
        var receiveTimeout = TimeSpan.FromHours(3);
        Start(receiveTimeout: receiveTimeout);
        var first = await SocketAsync(1);
        first.Receive(Ready);
        await NextStatusAsync(EventStreamState.Connected);
        await TestClock.Until(() => _clock.StartedTimers(receiveTimeout) == 2, "the feed waits for the next message");

        // At 1 h 54 the connection is kept.
        _clock.Advance(TimeSpan.FromMinutes(114));
        first.Receive("""{"id":"mower-1","type":"battery-event-v2","attributes":{}}""");
        await TestClock.Until(() => _clock.StartedTimers(receiveTimeout) == 3, "the feed waits for the next message");
        Assert.Equal(1, SocketCount);

        // Past the 115-minute limit, the next message is the last one on it.
        _clock.Advance(TimeSpan.FromMinutes(2));
        first.Receive("""{"id":"mower-1","type":"battery-event-v2","attributes":{}}""");

        var second = await SocketAsync(2);
        Assert.True(first.ClosedByClient);
        second.Receive(Ready);
        Assert.False((await NextStatusAsync(EventStreamState.Connected)).AfterOutage);
        Assert.DoesNotContain(_statuses, s => s.State == EventStreamState.Disconnected);
        Assert.Equal(2, EventCount);
    }

    [Fact]
    public async Task Closed_by_the_server_reconnects_at_once()
    {
        Start();
        var first = await SocketAsync(1);
        first.Receive(Ready);
        await NextStatusAsync(EventStreamState.Connected);

        first.CloseFromServer();

        (await SocketAsync(2)).Receive(Ready);
        Assert.False((await NextStatusAsync(EventStreamState.Connected)).AfterOutage);
        Assert.False(first.ClosedByClient);
        Assert.True(first.Disposed);
    }

    [Fact]
    public async Task Cancelling_closes_the_connection_and_ends_the_feed()
    {
        Start();
        var socket = await SocketAsync(1);
        socket.Receive(Ready);
        await NextStatusAsync(EventStreamState.Connected);

        await _stop.CancelAsync();
        await _run;

        Assert.True(socket.ClosedByClient);
        Assert.True(socket.Disposed);
    }

    private int SocketCount
    {
        get
        {
            lock (_sockets)
            {
                return _sockets.Count;
            }
        }
    }

    private int EventCount
    {
        get
        {
            lock (_events)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>The next connection attempts use these sockets, in order; the following ones accept the connection.</summary>
    private void Plan(params FakeWebSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            _plannedSockets.Enqueue(socket);
        }
    }

    private void Start(HttpStatusCode refuseAllWith = 0, TimeSpan? receiveTimeout = null)
    {
        var feed = new AutomowerEventFeed(_tokens, CreateSocket, _clock) { ReceiveTimeout = receiveTimeout ?? s_receiveTimeout };
        _run = Task.Run(() => feed.RunAsync(OnEventAsync, OnStatus, _stop.Token));

        FakeWebSocket CreateSocket()
        {
            var socket = _plannedSockets.Count > 0 ? _plannedSockets.Dequeue() : new FakeWebSocket(refuseAllWith);
            lock (_sockets)
            {
                _sockets.Add(socket);
            }
            return socket;
        }
    }

    private ValueTask OnEventAsync(AutomowerEvent evt)
    {
        lock (_events)
        {
            _events.Add(evt);
        }
        return ValueTask.CompletedTask;
    }

    private void OnStatus(EventStreamStatus status)
    {
        lock (_statuses)
        {
            _statuses.Add(status);
        }
    }

    /// <summary>Waits for the <paramref name="number"/>th connection attempt.</summary>
    private async Task<FakeWebSocket> SocketAsync(int number)
    {
        await TestClock.Until(() => SocketCount >= number, $"connection attempt {number}");
        lock (_sockets)
        {
            return _sockets[number - 1];
        }
    }

    /// <summary>Waits for the next status of <paramref name="state"/> after the last one returned.</summary>
    private async Task<EventStreamStatus> NextStatusAsync(EventStreamState state)
    {
        EventStreamStatus? found = null;
        await TestClock.Until(
            () =>
            {
                lock (_statuses)
                {
                    for (var i = _statusCursor; i < _statuses.Count; i++)
                    {
                        if (_statuses[i].State == state)
                        {
                            found = _statuses[i];
                            _statusCursor = i + 1;
                            return true;
                        }
                    }
                    return false;
                }
            },
            $"a {state} status");
        return found!;
    }
}
