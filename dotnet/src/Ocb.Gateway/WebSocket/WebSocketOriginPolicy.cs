namespace Ocb.Gateway.WebSocket;

/// <summary>
/// Validates WebSocket upgrade requests against a configured set of
/// allowed origins. Uses case-insensitive ordinal comparison.
/// </summary>
public sealed class WebSocketOriginPolicy
{
    private readonly HashSet<string> _allowed;

    public WebSocketOriginPolicy(string[] allowedOrigins)
    {
        _allowed = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the origin is present and matches an allowed entry.</summary>
    public bool IsAllowed(string? origin)
    {
        if (string.IsNullOrEmpty(origin)) return false;
        return _allowed.Contains(origin);
    }
}
