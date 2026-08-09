namespace Ocb.Channels.WebSocket;

/// <summary>
/// Serialises concurrent sends to a single consumer (e.g. a WebSocket
/// connection), ensuring frames are not interleaved.
/// </summary>
public sealed class SerialSendGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Enqueue a send operation. All sends are serialised so that frames
    /// from concurrent upstream messages do not interleave on the wire.
    /// </summary>
    public async ValueTask SendAsync(Func<ValueTask> send, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await send();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
