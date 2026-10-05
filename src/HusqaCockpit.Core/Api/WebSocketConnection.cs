using System.Net;
using System.Net.WebSockets;

namespace HusqaCockpit.Core.Api;

/// <summary>The few WebSocket operations <see cref="AutomowerEventFeed"/> uses, so that tests can replace the network.</summary>
internal interface IWebSocketConnection : IDisposable
{
    WebSocketState State { get; }

    /// <summary>The HTTP status the server answered the connection request with.</summary>
    HttpStatusCode HttpStatusCode { get; }

    Task ConnectAsync(Uri uri, string accessToken, CancellationToken cancellationToken);

    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);

    Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
}

/// <summary>The real connection: a <see cref="ClientWebSocket"/> authenticated with the access token.</summary>
internal sealed class ClientWebSocketConnection : IWebSocketConnection
{
    private readonly ClientWebSocket _socket = new();

    public ClientWebSocketConnection()
    {
        // Keeps HttpStatusCode available when the server refuses the connection (401, 403).
        _socket.Options.CollectHttpResponseDetails = true;
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    }

    public WebSocketState State => _socket.State;

    public HttpStatusCode HttpStatusCode => _socket.HttpStatusCode;

    public Task ConnectAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        return _socket.ConnectAsync(uri, cancellationToken);
    }

    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
        _socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

    public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

    public void Dispose() => _socket.Dispose();
}
