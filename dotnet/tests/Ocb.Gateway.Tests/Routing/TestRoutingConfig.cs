using Ocb.Gateway.Routing;

namespace Ocb.Gateway.Tests.Routing;

/// <summary>
/// Test helper that provides a realistic routing configuration aligned
/// with the parity corpus manifest (<c>dotnet/contracts/parity-corpus/manifest.json</c>).
/// </summary>
public static class TestRoutingConfig
{
    public static GatewayRoutingConfig Create() => new([
        // collaboration → bcs (general HTTP endpoints)
        new DomainRoute(
            Name: "collaboration",
            MatchPrefix: "/openapi/v1/collaboration",
            ServerName: "bcs",
            ServesHttp: true,
            ServesWebSocket: false,
            Rewrite: null
        ),
        // collaboration messages WS → bcsfuse (WebSocket relay)
        new DomainRoute(
            Name: "collaboration-ws",
            MatchPrefix: "/openapi/v1/collaboration/messages/ws",
            ServerName: "bcsfuse",
            ServesHttp: false,
            ServesWebSocket: true,
            Rewrite: null
        ),
        // bots → bots backend
        new DomainRoute(
            Name: "bots",
            MatchPrefix: "/openapi/v1/bots",
            ServerName: "bots",
            ServesHttp: true,
            ServesWebSocket: false,
            Rewrite: null
        ),
        // bcsfuse general → bcsfuse
        new DomainRoute(
            Name: "bcsfuse",
            MatchPrefix: "/openapi/v1/bcsfuse",
            ServerName: "bcsfuse",
            ServesHttp: true,
            ServesWebSocket: false,
            Rewrite: null
        ),
        // chat → engine
        new DomainRoute(
            Name: "chat",
            MatchPrefix: "/openapi/v1/chat",
            ServerName: "engine",
            ServesHttp: true,
            ServesWebSocket: false,
            Rewrite: null
        ),
    ]);
}
