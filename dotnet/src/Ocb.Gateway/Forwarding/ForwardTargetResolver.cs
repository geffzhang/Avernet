using Ocb.Gateway.Routing;

namespace Ocb.Gateway.Forwarding;

/// <summary>
/// Resolves a <see cref="DomainRoute"/> into a concrete upstream URI for
/// HTTP and WebSocket dials. Applies any path rewrite rule defined on the
/// route before constructing the target.
/// </summary>
public static class ForwardTargetResolver
{
    /// <summary>Resolve the upstream HTTP URI for a route, path, and query.</summary>
    public static Uri ResolveHttpUri(DomainRoute route, PathString path, QueryString query)
    {
        var rewritten = route.Rewrite is not null ? route.Rewrite.Rewrite(path) : (string)path;
        return new Uri($"http://{route.ServerName}{rewritten}{query}", UriKind.Absolute);
    }

    /// <summary>Resolve the upstream WebSocket URI for a route, path, and query.</summary>
    public static Uri ResolveWebSocketUri(DomainRoute route, PathString path, QueryString query)
    {
        var rewritten = route.Rewrite is not null ? route.Rewrite.Rewrite(path) : (string)path;
        return new Uri($"ws://{route.ServerName}{rewritten}{query}", UriKind.Absolute);
    }
}
