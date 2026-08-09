using System.Net.WebSockets;

namespace Ocb.Channels.Tests.WebSocket;

/// <summary>
/// A test-only <see cref="System.Net.WebSockets.WebSocket"/> implementation
/// backed by in-memory queues. Messages to deliver to the session are
/// enqueued via <see cref="EnqueueReceive"/>; messages the session sends
/// are captured in <see cref="SentMessages"/>.
/// </summary>
public sealed class FakeWebSocket : System.Net.WebSockets.WebSocket
{
    private readonly Queue<ReceiveEntry> _receiveQueue = new();
    private readonly List<SendEntry> _sends = [];

    public IReadOnlyList<SendEntry> SentMessages => _sends;
    public WebSocketCloseStatus? SentCloseStatus { get; private set; }
    public string? SentCloseDescription { get; private set; }
    public int CloseOutputCalledCount { get; private set; }

    private WebSocketState _state = WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeDescription;

    // --- State overrides ---

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeDescription;
    public override string? SubProtocol => null;

    public override WebSocketState State => _state;

    // --- Helpers to push data into the fake ---

    public void EnqueueReceive(ReadOnlyMemory<byte> data, WebSocketMessageType messageType, bool endOfMessage)
    {
        _receiveQueue.Enqueue(new ReceiveEntry(data.ToArray(), messageType, endOfMessage));
    }

    public void EnqueueClose()
    {
        _receiveQueue.Enqueue(new ReceiveEntry(
            Array.Empty<byte>(), WebSocketMessageType.Close, true, SetClose: true));
    }

    // --- Send (modern + legacy) ---

    public override ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        _sends.Add(new SendEntry(buffer.ToArray(), messageType, endOfMessage));
        return ValueTask.CompletedTask;
    }

    public override Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        _sends.Add(new SendEntry(buffer.ToArray(), messageType, endOfMessage));
        return Task.CompletedTask;
    }

    // --- Receive (modern + legacy) ---

    public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        return ReceiveCore(buffer);
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        var result = await ReceiveCore(buffer);
        return new WebSocketReceiveResult(
            result.Count, result.MessageType, result.EndOfMessage);
    }

    private ValueTask<ValueWebSocketReceiveResult> ReceiveCore(Memory<byte> buffer)
    {
        if (_receiveQueue.Count == 0)
        {
            return ValueTask.FromResult(new ValueWebSocketReceiveResult(
                0, WebSocketMessageType.Close, endOfMessage: true));
        }

        var entry = _receiveQueue.Dequeue();

        // Defer state change to when the entry is actually consumed.
        if (entry.SetClose)
        {
            _state = WebSocketState.CloseReceived;
            _closeStatus = WebSocketCloseStatus.NormalClosure;
            _closeDescription = "";
            return ValueTask.FromResult(new ValueWebSocketReceiveResult(
                0, WebSocketMessageType.Close, endOfMessage: true));
        }

        var count = Math.Min(entry.Data.Length, buffer.Length);
        entry.Data.AsMemory(0, count).CopyTo(buffer);
        return ValueTask.FromResult(new ValueWebSocketReceiveResult(
            count, entry.MessageType, entry.EndOfMessage));
    }

    // --- Close ---

    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        SentCloseStatus = closeStatus;
        SentCloseDescription = statusDescription;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        CloseOutputCalledCount++;
        if (CloseOutputCalledCount == 1)
        {
            SentCloseStatus = closeStatus;
            SentCloseDescription = statusDescription;
        }
        if (_state == WebSocketState.Open)
            _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override void Abort()
    {
        _state = WebSocketState.Aborted;
    }

    public override void Dispose()
    {
        _state = WebSocketState.Closed;
    }
}

public sealed record SendEntry(byte[] Data, WebSocketMessageType MessageType, bool EndOfMessage);

public sealed record ReceiveEntry(
    byte[] Data,
    WebSocketMessageType MessageType,
    bool EndOfMessage,
    bool SetClose = false
);
