using Ocb.Channels.WebSocket;

namespace Ocb.PluginApi;

/// <summary>
/// Connects to an upstream WebSocket service within a fixed handshake
/// timeout. Implementations own TCP dial, TLS, and header assembly.
/// </summary>
public interface IWebSocketUpstreamConnector : IPluginContract
{
    /// <summary>
    /// Open a duplex WebSocket connection to <paramref name="upstreamUri"/>.
    /// The caller wraps the result in <see cref="WebSocketChannelSession"/>.
    /// </summary>
    Task<IWebSocketUpstreamDuplex> ConnectAsync(
        Uri upstreamUri,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken);
}
