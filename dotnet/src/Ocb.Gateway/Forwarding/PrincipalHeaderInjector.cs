using Ocb.PluginApi;

namespace Ocb.Gateway.Forwarding;

/// <summary>
/// Prepares a <see cref="ForwardRequest"/> for upstream dial by stripping
/// the inbound X-Avernet-Principal and Host headers and injecting a freshly
/// signed principal token.
/// </summary>
public static class PrincipalHeaderInjector
{
    /// <summary>Header names that must be stripped before forwarding upstream.</summary>
    private static readonly string[] InboundStrip =
        ["X-Avernet-Principal", "Host"];

    /// <summary>
    /// Remove the inbound X-Avernet-Principal (prevents forgery), strip Host,
    /// and inject a freshly-signed principal token.
    /// </summary>
    public static ForwardRequest InjectSignedPrincipal(ForwardRequest request, string signedToken)
    {
        var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);

        foreach (var name in InboundStrip)
        {
            headers.Remove(name);
        }

        headers["X-Avernet-Principal"] = signedToken;

        return request with { Headers = headers };
    }
}
