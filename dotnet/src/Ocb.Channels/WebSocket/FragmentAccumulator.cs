using System.Buffers;
using System.Net.WebSockets;

namespace Ocb.Channels.WebSocket;

/// <summary>
/// Accumulates fragmented WebSocket messages into a single complete message.
/// Returns null when the client sends a Close frame.
/// </summary>
public sealed class FragmentAccumulator(int maxBytes)
{
    /// <summary>
    /// Read from the socket until a complete message arrives.
    /// Returns null for Close frames; throws when the accumulated message
    /// exceeds <paramref name="maxBytes"/>.
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>?> ReadMessageAsync(
        System.Net.WebSockets.WebSocket socket, CancellationToken cancellationToken)
    {
        using var buffer = MemoryPool<byte>.Shared.Rent(16 * 1024);
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.Memory, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            stream.Write(buffer.Memory.Span[..result.Count]);

            if (stream.Length > maxBytes)
                throw new InvalidOperationException("WebSocket message exceeds maximum allowed size.");

            if (result.EndOfMessage)
                return stream.ToArray();
        }
    }
}
