using System.Net;
using Ocb.Contracts;
using Ocb.PluginApi;

namespace Ocb.Gateway.Forwarding;

/// <summary>
/// ASP.NET Core endpoint handler that performs transparent HTTP forwarding
/// with signed principal injection, hop-by-hop header filtering, and
/// streaming response relay.
/// </summary>
public static class HttpForwardingEndpoint
{
    /// <summary>
    /// Forward the current HTTP request to the upstream service resolved
    /// via <paramref name="upstreamUri"/>.
    /// </summary>
    public static async Task HandleAsync(
        HttpContext context,
        IHttpForwarder forwarder,
        IPrincipalTokenSigner signer,
        IHopByHopHeaderFilter headerFilter,
        Uri upstreamUri)
    {
        var caller = context.Items[typeof(CallerContext)] as CallerContext
            ?? throw new InvalidOperationException("CallerContext is missing from HttpContext.Items.");

        var signed = await signer.SignAsync(caller, upstreamUri.Host, context.RequestAborted);

        var request = await ForwardRequestFactory.FromHttpContextAsync(context);
        request = PrincipalHeaderInjector.InjectSignedPrincipal(request, signed);

        // Strip hop-by-hop headers before forwarding.
        var filteredHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in request.Headers)
        {
            if (!headerFilter.ShouldStrip(name))
                filteredHeaders[name] = value;
        }
        request = request with { Headers = filteredHeaders };

        var response = await forwarder.ForwardAsync(request, context.RequestAborted);

        context.Response.StatusCode = response.StatusCode;
        foreach (var (name, value) in response.Headers)
        {
            if (!headerFilter.ShouldStrip(name))
                context.Response.Headers.Append(name, value);
        }

        await foreach (var chunk in response.Body.WithCancellation(context.RequestAborted))
        {
            await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
}
