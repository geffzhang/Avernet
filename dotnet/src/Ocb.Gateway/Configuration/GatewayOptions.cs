namespace Ocb.Gateway.Configuration;

public sealed record GatewayOptions(
    int HttpPort,
    int MaxConnections,
    int MaxConnectionsPerIp,
    int MessagesPerSecondPerConnection,
    string[] AllowedOrigins,
    string OrleansClusterId,
    string OrleansServiceId
);
