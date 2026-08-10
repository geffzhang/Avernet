using System.Text.Json;

namespace Ocb.Runtime.Worker.Api.WebSocket;

/// <summary>
/// Dispatches incoming WebSocket request frames to the correct handler.
/// Currently stubbed — all methods return fail-closed responses.
/// Full method implementations are deferred until engine backend integration.
/// </summary>
public sealed class EngineWsMethodDispatcher
{
    /// <summary>
    /// Dispatches a request frame and returns the response frame.
    /// Unknown methods or invalid frames return INVALID_REQUEST.
    /// </summary>
    public async Task<WsResponseFrame> DispatchAsync(
        WsRequestFrame request,
        CancellationToken ct)
    {
        if (!EngineWsProtocolGuards.IsAllowed(request.Method))
        {
            return EngineWsProtocolGuards.RejectUnknown(request.Id, request.Method);
        }

        return request.Method switch
        {
            "connect" => await HandleConnectAsync(request, ct),
            "health.claude" => HandleHealth(),
            _ => HandleStub(request),
        };
    }

    private static Task<WsResponseFrame> HandleConnectAsync(WsRequestFrame request, CancellationToken ct)
    {
        // Validate protocol version from connect params
        if (request.Params is { } p &&
            p.TryGetProperty("minProtocol", out var minProp) &&
            minProp.TryGetInt32(out var minProtocol))
        {
            var versionError = EngineWsProtocolGuards.ValidateProtocolVersion(minProtocol);
            if (versionError is not null)
                return Task.FromResult(versionError with { Id = request.Id });
        }

        return Task.FromResult(WsResponseFrame.Success(request.Id, new { status = "connected" }));
    }

    private static WsResponseFrame HandleHealth() =>
        WsResponseFrame.Success("health", new { status = "ok" });

    private static WsResponseFrame HandleStub(WsRequestFrame request) =>
        WsResponseFrame.Success(request.Id, new { acknowledged = true });
}
