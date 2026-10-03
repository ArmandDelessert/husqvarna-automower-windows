using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HusqaCockpit.Core.Api;

/// <summary>A real-time event pushed by the Automower WebSocket (e.g. "mower-event-v2").</summary>
public sealed record AutomowerEvent(string Type, string MowerId, JsonObject Attributes);

public enum EventStreamState
{
    Connecting,
    Connected,

    /// <summary>Connection lost; a reconnection attempt is scheduled.</summary>
    Disconnected,

    /// <summary>The server refused the connection (HTTP 403), usually an application/API-key configuration issue.</summary>
    Forbidden,
}

/// <param name="State">New state of the stream.</param>
/// <param name="AfterOutage">For <see cref="EventStreamState.Connected"/>: true when events may have been missed since the last connection.</param>
/// <param name="RetryAt">For disconnected states: when the next attempt will be made.</param>
public sealed record EventStreamStatus(EventStreamState State, bool AfterOutage = false, DateTimeOffset? RetryAt = null, string? Detail = null);

/// <summary>
/// Maintains the WebSocket connection to the Automower event service.
/// The server closes connections after two hours and after ten idle minutes, so the
/// stream sends a keep-alive every minute and reconnects proactively before the limit.
/// </summary>
public sealed class AutomowerEventStream
{
    public const string DefaultUri = "wss://ws.openapi.husqvarna.dev/v1";
    internal const string InvalidMowerId = "0-0";

    private static readonly TimeSpan[] s_backoff =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
    ];

    private readonly IAccessTokenProvider _tokens;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Uri _uri;

    public AutomowerEventStream(
        IAccessTokenProvider tokens,
        TimeProvider? time = null,
        ILogger<AutomowerEventStream>? logger = null,
        string uri = DefaultUri)
    {
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AutomowerEventStream>.Instance;
        _uri = new Uri(uri);
    }

    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Reconnect when nothing at all was received for this long.</summary>
    public TimeSpan ReceiveTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Reconnect proactively before the server's two-hour connection limit.</summary>
    public TimeSpan MaxConnectionAge { get; init; } = TimeSpan.FromMinutes(115);

    /// <summary>Wait before retrying after the server refused the connection.</summary>
    public TimeSpan ForbiddenRetryDelay { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled, reconnecting as needed.</summary>
    public async Task RunAsync(
        Func<AutomowerEvent, ValueTask> onEvent,
        Action<EventStreamStatus> onStatus,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        var outage = false;
        var retriedWithFreshToken = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            onStatus(new EventStreamStatus(EventStreamState.Connecting));
            TimeSpan retryDelay;
            try
            {
                var receivedAnything = await RunConnectionAsync(onEvent, onStatus, outage, cancellationToken).ConfigureAwait(false);
                // Clean end (proactive renewal or server close): reconnect immediately.
                failures = receivedAnything ? 0 : failures + 1;
                retriedWithFreshToken &= !receivedAnything;
                outage = !receivedAnything;
                retryDelay = receivedAnything ? TimeSpan.Zero : Backoff(failures);
                if (retryDelay > TimeSpan.Zero)
                {
                    onStatus(new EventStreamStatus(EventStreamState.Disconnected, RetryAt: _time.GetUtcNow() + retryDelay));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ForbiddenException) when (!retriedWithFreshToken)
            {
                // A token issued before the application was connected to the Automower Connect API lacks
                // the scope required here (REST still works with it): retry once with a new token.
                _logger.LogInformation("Event stream refused (403), retrying with a new access token");
                _tokens.Invalidate();
                retriedWithFreshToken = true;
                outage = true;
                retryDelay = TimeSpan.FromSeconds(5);
            }
            catch (ForbiddenException ex)
            {
                _logger.LogWarning("Event stream refused (403); the application may lack access to the Automower Connect API events");
                outage = true;
                retryDelay = ForbiddenRetryDelay;
                onStatus(new EventStreamStatus(EventStreamState.Forbidden, RetryAt: _time.GetUtcNow() + retryDelay, Detail: ex.Message));
            }
            catch (Exception ex)
            {
                failures++;
                outage = true;
                retryDelay = Backoff(failures);
                _logger.LogWarning(ex, "Event stream failed, retrying in {Delay}", retryDelay);
                onStatus(new EventStreamStatus(EventStreamState.Disconnected, RetryAt: _time.GetUtcNow() + retryDelay, Detail: ex.Message));
            }

            if (retryDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(retryDelay, _time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static TimeSpan Backoff(int failures) => s_backoff[Math.Clamp(failures - 1, 0, s_backoff.Length - 1)];

    /// <returns>True if the connection was established and received at least one message.</returns>
    private async Task<bool> RunConnectionAsync(
        Func<AutomowerEvent, ValueTask> onEvent,
        Action<EventStreamStatus> onStatus,
        bool afterOutage,
        CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        try
        {
            await socket.ConnectAsync(_uri, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException) when (socket.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            _tokens.Invalidate();
            throw;
        }
        catch (WebSocketException ex) when (socket.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            throw new ForbiddenException(ex);
        }

        _logger.LogInformation("Event stream connected");
        var connectedAt = _time.GetUtcNow();
        var receivedAnything = false;
        var announced = false;

        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var keepAlive = KeepAliveLoopAsync(socket, connectionCts.Token);
        try
        {
            var buffer = new byte[16 * 1024];
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                if (_time.GetUtcNow() - connectedAt > MaxConnectionAge)
                {
                    _logger.LogDebug("Renewing event stream connection before the server limit");
                    break;
                }

                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(ReceiveTimeout);
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, receiveCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("No data received for {Timeout}, reconnecting", ReceiveTimeout);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Event stream closed by server: {Status} {Description}", result.CloseStatus, result.CloseStatusDescription);
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                receivedAnything = true;

                if (!announced)
                {
                    // The server greets with {"ready":true,...}; any message proves the connection works.
                    announced = true;
                    onStatus(new EventStreamStatus(EventStreamState.Connected, AfterOutage: afterOutage));
                }

                if (ParseEvent(text) is { } evt)
                {
                    await onEvent(evt).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await connectionCts.CancelAsync().ConfigureAwait(false);
            await keepAlive.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (socket.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
                {
                    // Best effort.
                }
            }
        }

        return receivedAnything;
    }

    private async Task KeepAliveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await Task.Delay(KeepAliveInterval, _time, cancellationToken).ConfigureAwait(false);
            // An empty text message keeps the connection from idling out; the server answers with an empty message.
            await socket.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Parses a WebSocket text frame. Returns null for keep-alives, the "ready" greeting and unknown payloads.</summary>
    internal static AutomowerEvent? ParseEvent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject root
                || root["type"]?.GetValue<string>() is not { } type
                || root["id"]?.GetValue<string>() is not { } id
                || id == InvalidMowerId)
            {
                return null;
            }

            var attributes = root["attributes"] as JsonObject ?? [];
            root.Remove("attributes");
            return new AutomowerEvent(type, id, attributes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class ForbiddenException(Exception inner)
        : Exception("The event service refused the connection (HTTP 403).", inner);
}
