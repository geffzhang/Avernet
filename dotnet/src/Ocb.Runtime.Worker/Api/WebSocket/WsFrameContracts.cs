using System.Text.Json.Serialization;

namespace Ocb.Runtime.Worker.Api.WebSocket;

/// <summary>
/// Wire-level WebSocket frame DTOs matching the engine WebSocket protocol
/// defined in dotnet/contracts/parity-corpus/engine-websocket-protocol.md.
/// </summary>

public sealed record WsRequestFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] System.Text.Json.JsonElement? Params
);

public sealed record WsResponseFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("payload")] System.Text.Json.JsonElement? Payload,
    [property: JsonPropertyName("error")] WsErrorDetail? Error
)
{
    public static WsResponseFrame Success(string id, object payload) =>
        new("res", id, true,
            System.Text.Json.JsonSerializer.SerializeToElement(payload),
            null);

    public static WsResponseFrame ErrorResponse(string id, string code, string message) =>
        new("res", id, false, null,
            new WsErrorDetail(code, message, null, false));
}

public sealed record WsEventFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("payload")] System.Text.Json.JsonElement? Payload,
    [property: JsonPropertyName("seq")] int Seq
);

public sealed record WsErrorDetail(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] System.Text.Json.JsonElement? Details,
    [property: JsonPropertyName("retryable")] bool Retryable
);
