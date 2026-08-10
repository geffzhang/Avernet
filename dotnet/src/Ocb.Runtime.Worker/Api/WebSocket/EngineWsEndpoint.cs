using System.Net;
using System.Text.Json;

namespace Ocb.Runtime.Worker.Api.WebSocket;

/// <summary>
/// Engine WebSocket endpoint — serves the engine WebSocket protocol
/// on the /api/openclaw/ws route.
///
/// Handles connection admission, protocol version negotiation,
/// method dispatch, and error semantics matching the parity corpus.
/// </summary>
public static class EngineWsEndpoint
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Maps the engine WebSocket route onto the application.
    /// </summary>
    public static void MapRoutes(WebApplication app, EngineWsConnectionAdmissionGate admissionGate)
    {
        app.Map("/api/openclaw/ws", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync("WebSocket connection expected.", context.RequestAborted);
                return;
            }

            // Connection admission — reject with 503 if at capacity
            if (!admissionGate.TryAcquire())
            {
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                context.Response.Headers.RetryAfter = "30";
                return;
            }

            try
            {
                using var ws = await context.WebSockets.AcceptWebSocketAsync();

                // Send connect.challenge event
                var challenge = new WsEventFrame(
                    "event", "connect.challenge",
                    JsonSerializer.SerializeToElement(new
                    {
                        challenge = Guid.NewGuid().ToString("N"),
                        minProtocol = EngineWsProtocolGuards.ProtocolVersion,
                        maxProtocol = EngineWsProtocolGuards.ProtocolVersion,
                    }),
                    Seq: 0);

                var challengeBytes = JsonSerializer.SerializeToUtf8Bytes(challenge, SerializerOptions);
                await ws.SendAsync(
                    challengeBytes,
                    System.Net.WebSockets.WebSocketMessageType.Text,
                    endOfMessage: true,
                    context.RequestAborted);

                var dispatcher = new EngineWsMethodDispatcher();
                var buffer = new byte[4096];

                while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, context.RequestAborted);

                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(
                            System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            context.RequestAborted);
                        break;
                    }

                    if (result.MessageType != System.Net.WebSockets.WebSocketMessageType.Text)
                        continue;

                    var text = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);

                    WsRequestFrame? request;
                    try
                    {
                        request = JsonSerializer.Deserialize<WsRequestFrame>(text, SerializerOptions);
                    }
                    catch (JsonException)
                    {
                        var errFrame = WsResponseFrame.ErrorResponse(
                            id: "unknown",
                            code: "INVALID_REQUEST",
                            message: "Failed to parse request frame.");

                        var errBytes = JsonSerializer.SerializeToUtf8Bytes(errFrame, SerializerOptions);
                        await ws.SendAsync(errBytes, System.Net.WebSockets.WebSocketMessageType.Text,
                            endOfMessage: true, context.RequestAborted);
                        continue;
                    }

                    if (request is null || request.Type != "req")
                        continue;

                    var response = await dispatcher.DispatchAsync(request, context.RequestAborted);
                    var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions);

                    await ws.SendAsync(
                        responseBytes,
                        System.Net.WebSockets.WebSocketMessageType.Text,
                        endOfMessage: true,
                        context.RequestAborted);
                }
            }
            finally
            {
                admissionGate.Release();
            }
        });
    }
}
