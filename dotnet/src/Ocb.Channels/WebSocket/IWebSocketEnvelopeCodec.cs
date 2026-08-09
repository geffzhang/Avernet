namespace Ocb.Channels.WebSocket;

/// <summary>
/// Codec for wrapping/unwrapping WebSocket message payloads in an envelope
/// format. Raw codec passes bytes through unchanged; JSON codec wraps them
/// in a lightweight JSON envelope.
/// </summary>
public interface IWebSocketEnvelopeCodec
{
    /// <summary>Encode a raw payload for transmission over the WebSocket.</summary>
    ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> payload);

    /// <summary>Decode a WebSocket frame into its raw payload.</summary>
    ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> frame);
}
