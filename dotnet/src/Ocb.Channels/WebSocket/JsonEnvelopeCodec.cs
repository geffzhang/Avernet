using System.Text;
using System.Text.Json;

namespace Ocb.Channels.WebSocket;

/// <summary>
/// Wraps raw payloads in a lightweight JSON envelope:
/// <c>{"type":"message","payload":"base64..."}</c>
/// </summary>
public sealed class JsonEnvelopeCodec : IWebSocketEnvelopeCodec
{
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> payload)
    {
        var base64 = Convert.ToBase64String(payload.Span);
        var envelope = JsonSerializer.Serialize(new { type = "message", payload = base64 });
        return Encoding.UTF8.GetBytes(envelope);
    }

    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> frame)
    {
        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;
        if (root.TryGetProperty("payload", out var payloadElement)
            && payloadElement.GetString() is { } base64)
        {
            return Convert.FromBase64String(base64);
        }
        // Not a valid envelope — pass through unchanged.
        return frame;
    }
}
