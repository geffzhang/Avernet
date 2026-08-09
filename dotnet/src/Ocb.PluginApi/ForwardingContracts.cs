using Ocb.Contracts;

namespace Ocb.PluginApi;

/// <summary>
/// Signs a CallerContext into a signed principal token that the gateway
/// includes in the X-Avernet-Principal header when forwarding upstream.
/// </summary>
public interface IPrincipalTokenSigner : IPluginContract
{
    ValueTask<string> SignAsync(CallerContext callerContext, string audience, CancellationToken cancellationToken);
}

/// <summary>
/// Filters hop-by-hop headers that must not be forwarded. These headers
/// are meaningful only for the single transport-level connection and must
/// be stripped before the request is sent upstream and before the response
/// is returned to the caller.
/// </summary>
public interface IHopByHopHeaderFilter : IPluginContract
{
    /// <summary>The set of hop-by-hop header names (case-insensitive).</summary>
    IReadOnlySet<string> HopByHopHeaders { get; }

    /// <summary>True when the header should be stripped from the forwarded message.</summary>
    bool ShouldStrip(string headerName);
}

/// <summary>
/// Forwards an HTTP request to an upstream service and returns the
/// response as a streamable result.
/// </summary>
public interface IHttpForwarder : IPluginContract
{
    ValueTask<ForwardResponse> ForwardAsync(ForwardRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The result of forwarding an HTTP request to an upstream service.
/// </summary>
public sealed record ForwardResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    IAsyncEnumerable<byte[]> Body
);

/// <summary>
/// A normalised HTTP forward request ready to be sent to an upstream service.
/// </summary>
public sealed record ForwardRequest(
    string Method,
    string Path,
    string Query,
    IReadOnlyDictionary<string, string> Headers,
    Stream? Body
);
