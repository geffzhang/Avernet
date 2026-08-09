using Ocb.Gateway.Routing;

namespace Ocb.Gateway.Tests.Routing;

public sealed class DomainMapTests
{
    [Fact]
    public void CollaborationPathResolvesToBcsDomain()
    {
        var config = TestRoutingConfig.Create();
        var map = DomainMap.FromGatewayConfig(config);

        var route = map.ResolveHttp("/openapi/v1/collaboration/bots/mine");

        Assert.NotNull(route);
        Assert.Equal("bcs", route!.ServerName);
        Assert.True(route.ServesHttp);
    }

    [Fact]
    public void ExactPrefixMatchReturnsRoute()
    {
        var config = new GatewayRoutingConfig([
            new DomainRoute("bots", "/openapi/v1/bots", "bots-backend",
                ServesHttp: true, ServesWebSocket: false, Rewrite: null)
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        var route = map.ResolveHttp("/openapi/v1/bots");

        Assert.NotNull(route);
        Assert.Equal("bots-backend", route!.ServerName);
    }

    [Fact]
    public void SubPathMatchReturnsRoute()
    {
        var config = new GatewayRoutingConfig([
            new DomainRoute("bots", "/openapi/v1/bots", "bots-backend",
                ServesHttp: true, ServesWebSocket: false, Rewrite: null)
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        var route = map.ResolveHttp("/openapi/v1/bots/mine");

        Assert.NotNull(route);
        Assert.Equal("bots-backend", route!.ServerName);
    }

    [Fact]
    public void NonMatchingPrefixReturnsNull()
    {
        var config = new GatewayRoutingConfig([
            new DomainRoute("bots", "/openapi/v1/bots", "bots-backend",
                ServesHttp: true, ServesWebSocket: false, Rewrite: null)
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        var route = map.ResolveHttp("/openapi/v1/chat/stream");

        Assert.Null(route);
    }

    [Fact]
    public void WebsocketOnlyRouteDoesNotMatchHttp()
    {
        var config = new GatewayRoutingConfig([
            new DomainRoute("ws-collab", "/openapi/v1/collaboration/messages/ws", "bcsfuse",
                ServesHttp: false, ServesWebSocket: true, Rewrite: null)
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        Assert.Null(map.ResolveHttp("/openapi/v1/collaboration/messages/ws"));
        Assert.NotNull(map.ResolveWebSocket("/openapi/v1/collaboration/messages/ws"));
    }

    [Fact]
    public void LongestPrefixWinsWhenMultipleMatches()
    {
        var config = new GatewayRoutingConfig([
            new DomainRoute("bcs-general", "/openapi/v1/bcsfuse", "bcsfuse-general",
                ServesHttp: true, ServesWebSocket: true, Rewrite: null),
            new DomainRoute("bcs-fuse", "/openapi/v1/bcsfuse/groups", "bcsfuse-groups",
                ServesHttp: true, ServesWebSocket: true, Rewrite: null),
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        var route = map.ResolveHttp("/openapi/v1/bcsfuse/groups/g1/fuse");

        Assert.NotNull(route);
        Assert.Equal("bcsfuse-groups", route!.ServerName);
    }

    [Fact]
    public void PartialWordInPathDoesNotMatchPrefix()
    {
        var config = new GatewayRoutingConfig([
            new DomainRoute("bots", "/openapi/v1/bots", "bots-backend",
                ServesHttp: true, ServesWebSocket: false, Rewrite: null)
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        // "botsuffix" should NOT match "/openapi/v1/bots" prefix
        var route = map.ResolveHttp("/openapi/v1/botsuffix/mine");

        Assert.Null(route);
    }

    [Fact]
    public void RouteWithRewriteReturnsRewriteRule()
    {
        var rewrite = new PathRewriteRule("/openapi/v1/bots", "/api");
        var config = new GatewayRoutingConfig([
            new DomainRoute("bots", "/openapi/v1/bots", "bots-backend",
                ServesHttp: true, ServesWebSocket: false, Rewrite: rewrite)
        ]);
        var map = DomainMap.FromGatewayConfig(config);

        var route = map.ResolveHttp("/openapi/v1/bots/mine");

        Assert.NotNull(route);
        Assert.NotNull(route!.Rewrite);
        Assert.Equal("/api/mine", route.Rewrite!.Rewrite("/openapi/v1/bots/mine"));
    }
}
