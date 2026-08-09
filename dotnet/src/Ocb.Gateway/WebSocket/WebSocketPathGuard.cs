namespace Ocb.Gateway.WebSocket;

/// <summary>
/// Guards against path traversal and encoded-path bypass attacks on
/// WebSocket upgrade requests. Aligned with Python
/// <c>_relay_ws.py</c> <c>_has_dot_segment</c> and
/// <c>_required_raw_prefix</c>.
/// </summary>
public static class WebSocketPathGuard
{
    /// <summary>
    /// Returns true when <paramref name="decodedPath"/> contains a "."
    /// or ".." segment. Refusing here does not depend on assuming which
    /// component normalises — the guard blocks traversal before any
    /// downstream handler can interpret the path.
    /// </summary>
    public static bool HasDotSegment(ReadOnlySpan<char> decodedPath)
    {
        // Split the path by '/' and check each segment for "." or ".."
        foreach (var segment in decodedPath.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") return true;
        }
        return false;
    }

    /// <summary>
    /// Ensures the raw (percent-encoded) path sent to the upstream
    /// contains the expected domain prefix verbatim — not encoded.
    /// Prevents "authorised as one resource, dialled as another"
    /// attacks where an attacker encodes the prefix to bypass routing
    /// while reaching a different upstream resource.
    /// </summary>
    public static bool HasRequiredRawPrefix(
        ReadOnlySpan<char> decodedPath,
        ReadOnlySpan<char> rawPath,
        string domainPrefix)
    {
        if (string.IsNullOrEmpty(domainPrefix)) return true;

        var raw = rawPath.ToString();

        // The raw path must start with "/{domainPrefix}" or "{domainPrefix}"
        if (raw.StartsWith("/" + domainPrefix, StringComparison.Ordinal)
            || raw.StartsWith(domainPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
