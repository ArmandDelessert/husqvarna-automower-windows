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

/// <summary>The real-time events, as <see cref="Fleet.FleetMonitor"/> consumes them.</summary>
public interface IAutomowerEventFeed
{
    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled, reconnecting as needed.</summary>
    Task RunAsync(Func<AutomowerEvent, ValueTask> onEvent, Action<EventStreamStatus> onStatus, CancellationToken cancellationToken);
}

/// <summary>
/// Maintains the WebSocket connection to the Automower event service.
/// The server closes connections after two hours and after ten idle minutes, so the
/// stream sends a keep-alive every minute and reconnects proactively before the limit.
/// </summary>
public sealed partial class AutomowerEventFeed : IAutomowerEventFeed
{
    public const string DefaultUri = "wss://ws.openapi.husqvarna.dev/v1";
    internal const string InvalidMowerId = "0-0";

    private static readonly TimeSpan[] s_backoff =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
    ];

    private readonly IAccessTokenProvider _tokens;
    private readonly Func<IWebSocketConnection> _createSocket;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Uri _uri;

    public AutomowerEventFeed(
        IAccessTokenProvider tokens,
        TimeProvider? time = null,
        ILogger<AutomowerEventFeed>? logger = null,
        string uri = DefaultUri)
        : this(tokens, () => new ClientWebSocketConnection(), time, logger, uri)
    {
    }

    /// <param name="createSocket">Creates the socket of each connection attempt (a fake one in the tests).</param>
    internal AutomowerEventFeed(
        IAccessTokenProvider tokens,
        Func<IWebSocketConnection> createSocket,
        TimeProvider? time = null,
        ILogger<AutomowerEventFeed>? logger = null,
        string uri = DefaultUri)
    {
        _tokens = tokens;
        _createSocket = createSocket;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AutomowerEventFeed>.Instance;
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
                var (receivedAnything, wentSilent) = await RunConnectionAsync(onEvent, onStatus, outage, cancellationToken).ConfigureAwait(false);
                // Clean end (proactive renewal or server close): reconnect immediately.
                failures = receivedAnything ? 0 : failures + 1;
                retriedWithFreshToken &= !receivedAnything;
                // A connection that went silent may have lost events before it was given up on:
                // the next one reports an outage, so the fleet is refreshed at once.
                outage = !receivedAnything || wentSilent;
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
                LogForbiddenRetryingWithNewToken(_logger);
                _tokens.Invalidate();
                retriedWithFreshToken = true;
                outage = true;
                retryDelay = TimeSpan.FromSeconds(5);
            }
            catch (ForbiddenException ex)
            {
                LogForbidden(_logger);
                outage = true;
                retryDelay = ForbiddenRetryDelay;
                onStatus(new EventStreamStatus(EventStreamState.Forbidden, RetryAt: _time.GetUtcNow() + retryDelay, Detail: ex.Message));
            }
            catch (Exception ex)
            {
                failures++;
                outage = true;
                retryDelay = Backoff(failures);
                LogFailed(_logger, ex, retryDelay);
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

    /// <returns>
    /// Whether the connection was established and received at least one message, and whether it
    /// was given up on because nothing arrived for <see cref="ReceiveTimeout"/>.
    /// </returns>
    private async Task<(bool ReceivedAnything, bool WentSilent)> RunConnectionAsync(
        Func<AutomowerEvent, ValueTask> onEvent,
        Action<EventStreamStatus> onStatus,
        bool afterOutage,
        CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        using var socket = _createSocket();
        try
        {
            await socket.ConnectAsync(_uri, token, cancellationToken).ConfigureAwait(false);
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

        LogConnected(_logger);
        var connectedAt = _time.GetUtcNow();
        var receivedAnything = false;
        var wentSilent = false;
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
                    LogRenewing(_logger);
                    break;
                }

                using var timeout = new CancellationTokenSource(ReceiveTimeout, _time);
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, receiveCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    LogReceiveTimeout(_logger, ReceiveTimeout);
                    wentSilent = true;
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    LogClosedByServer(_logger, result.CloseStatus, result.CloseStatusDescription);
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
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5), _time);
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

        return (receivedAnything, wentSilent);
    }

    private async Task KeepAliveLoopAsync(IWebSocketConnection socket, CancellationToken cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Event stream refused (403), retrying with a new access token")]
    private static partial void LogForbiddenRetryingWithNewToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event stream refused (403); the application may lack access to the Automower Connect API events")]
    private static partial void LogForbidden(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event stream failed, retrying in {Delay}")]
    private static partial void LogFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Event stream connected")]
    private static partial void LogConnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Renewing event stream connection before the server limit")]
    private static partial void LogRenewing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No data received for {Timeout}, reconnecting")]
    private static partial void LogReceiveTimeout(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Event stream closed by server: {Status} {Description}")]
    private static partial void LogClosedByServer(ILogger logger, WebSocketCloseStatus? status, string? description);

    private sealed class ForbiddenException(Exception inner)
        : Exception("The event service refused the connection (HTTP 403).", inner);
}
