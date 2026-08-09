using System.Threading.Channels;

namespace Ocb.Gateway.Sse;

/// <summary>
/// Bounded pump that gates writes to an SSE stream.
/// When the consumer is too slow the bounded channel applies natural
/// backpressure; the writer can observe <c>TryWriteAsync</c> returning
/// <c>false</c> and choose to drop or retry.
/// </summary>
public sealed class SseBackpressurePump : IDisposable
{
    private readonly Channel<SseEvent> _channel;

    public SseBackpressurePump(int capacity)
    {
        _channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// Attempt to write an event without blocking.
    /// Returns <c>false</c> when the channel is full (backpressure signal).
    /// </summary>
    public ValueTask<bool> TryWriteAsync(SseEvent evt, CancellationToken cancellationToken)
        => ValueTask.FromResult(_channel.Writer.TryWrite(evt));

    /// <summary>
    /// Read events as an async stream until cancellation or
    /// <see cref="ChannelWriter{T}.Complete"/> is called.
    /// </summary>
    public IAsyncEnumerable<SseEvent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Mark the pump as complete — no more events will be written.
    /// The consumer's <see cref="ReadAllAsync"/> will finish after the
    /// last buffered event.
    /// </summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// Number of events currently buffered and waiting to be sent.
    /// </summary>
    public int Count => _channel.Reader.Count;

    /// <summary>
    /// Complete the underlying channel so the consumer finishes after
    /// draining buffered events.
    /// </summary>
    public void Dispose() => _channel.Writer.TryComplete();
}
