using System.Net.WebSockets;
using System.Threading.Channels;
using Ocb.Channels.WebSocket;

namespace Ocb.Channels.Tests.WebSocket;

/// <summary>
/// A test-only <see cref="IWebSocketUpstreamDuplex"/> that uses channels
/// for in-memory bidirectional communication.
/// </summary>
public sealed class FakeUpstreamDuplex : IWebSocketUpstreamDuplex
{
    private readonly Channel<ReadOnlyMemory<byte>> _inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
    private readonly List<byte[]> _sentMessages = [];
    private int _closeCount;
    private WebSocketCloseStatus _lastCloseStatus;
    private string _lastCloseMessage = "";

    public IReadOnlyList<byte[]> SentMessages => _sentMessages;
    public int CloseCount => _closeCount;
    public WebSocketCloseStatus LastCloseStatus => _lastCloseStatus;
    public string LastCloseMessage => _lastCloseMessage;

    /// <summary>Push a message FROM upstream TO be relayed to the client.</summary>
    public void WriteInbound(byte[] data) => _inbound.Writer.TryWrite(data);

    /// <summary>Complete the inbound stream.</summary>
    public void CompleteInbound() => _inbound.Writer.Complete();

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _sentMessages.Add(payload.ToArray());
        await Task.CompletedTask;
    }

    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken)
        => _inbound.Reader.ReadAllAsync(cancellationToken);

    public async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _closeCount);
        _lastCloseStatus = status;
        _lastCloseMessage = description;
        // Signal the reader to stop so that ReceiveAsync completes.
        _inbound.Writer.TryComplete();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        await Task.CompletedTask;
    }
}
