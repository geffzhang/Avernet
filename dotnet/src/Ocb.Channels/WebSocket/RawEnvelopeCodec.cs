namespace Ocb.Channels.WebSocket;

/// <summary>
/// Passes raw bytes through unchanged — no envelope wrapping.
/// </summary>
public sealed class RawEnvelopeCodec : IWebSocketEnvelopeCodec
{
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> payload) => payload;

    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> frame) => frame;
}
