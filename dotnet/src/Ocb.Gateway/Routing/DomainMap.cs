namespace Ocb.Gateway.Routing;

/// <summary>
/// A single route entry mapping an incoming path prefix to a backend domain.
/// </summary>
public sealed record DomainRoute(
    string Name,
    string MatchPrefix,
    string ServerName,
    bool ServesHttp,
    bool ServesWebSocket,
    PathRewriteRule? Rewrite
);

/// <summary>
/// Longest-prefix routing table that maps incoming paths to backend
/// domain routes. Routes are ordered descending by prefix length so
/// that more-specific prefixes are matched first.
/// </summary>
public sealed class DomainMap
{
    private readonly IReadOnlyList<DomainRoute> _routes;

    private DomainMap(IReadOnlyList<DomainRoute> routes)
        => _routes = routes;

    /// <summary>Build a routing table from configuration.</summary>
    public static DomainMap FromGatewayConfig(GatewayRoutingConfig config)
        => new(config.Routes
            .OrderByDescending(r => r.MatchPrefix.Length)
            .ToArray());

    /// <summary>Resolve the best HTTP route for the given path.</summary>
    public DomainRoute? ResolveHttp(string path)
        => _routes.FirstOrDefault(r => r.ServesHttp && PathStarts(path, r.MatchPrefix));

    /// <summary>Resolve the best WebSocket route for the given path.</summary>
    public DomainRoute? ResolveWebSocket(string path)
        => _routes.FirstOrDefault(r => r.ServesWebSocket && PathStarts(path, r.MatchPrefix));

    private static bool PathStarts(string path, string prefix)
        => path.Equals(prefix, StringComparison.Ordinal)
        || path.StartsWith(prefix + "/", StringComparison.Ordinal);
}
