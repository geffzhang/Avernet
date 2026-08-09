using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Features;
using Ocb.Channels.WebSocket;
using Ocb.Gateway.Forwarding;
using Ocb.Gateway.Routing;
using Ocb.PluginApi;

namespace Ocb.Gateway.WebSocket;

/// <summary>
/// ASP.NET Core endpoint adapter that accepts a WebSocket upgrade,
/// validates origin and path safety, resolves the upstream route,
/// and starts the bidirectional relay via
/// <see cref="WebSocketChannelSession"/>.
/// </summary>
public static class GatewayWebSocketEndpoint
{
    /// <summary>
    /// Handshake timeout for upstream WebSocket connections.
    /// Aligned with Python <c>_ws_forwarder.py:45</c>.
    /// Tests may override this to avoid waiting for the real 10 s timeout.
    /// </summary>
    internal static TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Handle a WebSocket upgrade request: validate origin, guard
    /// against path traversal and encoded prefix bypass, resolve
    /// the upstream route, and relay bidirectionally.
    /// </summary>
    public static async Task HandleAsync(HttpContext context)
    {
        // 1. Origin validation
        var originPolicy = context.RequestServices.GetRequiredService<WebSocketOriginPolicy>();
        var origin = context.Request.Headers.Origin.ToString();
        if (!originPolicy.IsAllowed(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // 2. Path traversal guard — block "." and ".." segments
        var decodedPath = context.Request.Path.Value ?? string.Empty;
        if (WebSocketPathGuard.HasDotSegment(decodedPath))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // 3. Resolve the WebSocket route
        var domainMap = context.RequestServices.GetRequiredService<DomainMap>();
        var route = domainMap.ResolveWebSocket(context.Request.Path);
        if (route is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // 4. Raw path encoding guard — ensure the domain prefix is
        //    present verbatim in the raw (percent-encoded) path
        var rawFromFeature = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        var rawTarget = !string.IsNullOrEmpty(rawFromFeature)
            ? rawFromFeature
            : context.Request.Path.ToString();
        if (!WebSocketPathGuard.HasRequiredRawPrefix(decodedPath, rawTarget, route.MatchPrefix.TrimStart('/')))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // 5. Resolve the upstream URI
        var upstreamUri = ForwardTargetResolver.ResolveWebSocketUri(
            route, context.Request.Path, context.Request.QueryString);

        // 6. Accept the client WebSocket
        using var clientSocket = await context.WebSockets.AcceptWebSocketAsync();

        // 7. Connect to upstream with timeout
        var connector = context.RequestServices.GetRequiredService<IWebSocketUpstreamConnector>();
        IWebSocketUpstreamDuplex upstream;
        using var timeoutCts = new CancellationTokenSource(HandshakeTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, context.RequestAborted);
        try
        {
            upstream = await connector.ConnectAsync(
                upstreamUri,
                new Dictionary<string, string>(),
                HandshakeTimeout,
                linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                  && !context.RequestAborted.IsCancellationRequested)
        {
            await clientSocket.CloseOutputAsync(
                WebSocketCloseStatus.EndpointUnavailable,
                "upstream handshake timeout",
                CancellationToken.None);
            return;
        }
        catch (Exception)
        {
            await clientSocket.CloseOutputAsync(
                WebSocketCloseStatus.InternalServerError,
                "upstream connection failed",
                CancellationToken.None);
            return;
        }

        await using (upstream)
        {
            var session = context.RequestServices.GetRequiredService<WebSocketChannelSession>();
            await session.RunAsync(clientSocket, upstream, context.RequestAborted);
        }
    }
}
