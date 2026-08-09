namespace Ocb.Channels.WebSocket;

/// <summary>
/// Configuration for a WebSocket channel session.
/// </summary>
public sealed record WebSocketChannelOptions(
    int MaxConnections,
    int MaxConnectionsPerIp,
    int MessagesPerSecondPerConnection,
    int MaxMessageBytes,
    bool EnableJsonEnvelope
);
