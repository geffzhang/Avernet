namespace Ocb.Gateway.Routing;

/// <summary>
/// A path-rewrite rule for domain forwarding.
/// The gateway strips <see cref="MatchPrefix"/> from the incoming path
/// and replaces it with <see cref="ReplaceWith"/> before dialling the
/// upstream service.
/// </summary>
public sealed record PathRewriteRule(string MatchPrefix, string ReplaceWith)
{
    /// <summary>Apply this rule to an incoming path.</summary>
    public string Rewrite(string path)
    {
        if (!path.StartsWith(MatchPrefix, StringComparison.Ordinal))
            return path;
        return ReplaceWith + path[MatchPrefix.Length..];
    }
}
