using System.Net.WebSockets;

namespace Ocb.Channels.WebSocket;

/// <summary>
/// Abstraction over an upstream WebSocket connection, used by
/// <see cref="WebSocketChannelSession"/> for bidirectional relay.
/// </summary>
public interface IWebSocketUpstreamDuplex : IAsyncDisposable
{
    /// <summary>Send a payload frame upstream.</summary>
    Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>Receive payload frames from upstream as an async stream.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Close the upstream connection.</summary>
    Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken);
}
