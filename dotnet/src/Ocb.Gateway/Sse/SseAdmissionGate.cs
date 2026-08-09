namespace Ocb.Gateway.Sse;

/// <summary>
/// Limits concurrent SSE streams so that the gateway does not exhaust
/// thread-pool or memory under load.  Exceeding the limit returns 429
/// <em>before</em> the response starts, so the client can retry safely.
/// </summary>
public sealed class SseAdmissionGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public SseAdmissionGate(int maxActiveStreams)
    {
        _semaphore = new SemaphoreSlim(maxActiveStreams, maxActiveStreams);
    }

    /// <summary>
    /// Try to acquire a slot.  Returns <c>true</c> together with a
    /// <see cref="IDisposable"/> lease; disposing the lease releases
    /// the slot back to the gate.
    /// </summary>
    public bool TryAcquire(out IDisposable? lease)
    {
        if (_semaphore.Wait(millisecondsTimeout: 0))
        {
            lease = new AdmissionLease(this);
            return true;
        }

        lease = null;
        return false;
    }

    internal void Release() => _semaphore.Release();

    /// <inheritdoc />
    public void Dispose() => _semaphore.Dispose();

    private sealed class AdmissionLease : IDisposable
    {
        private SseAdmissionGate? _gate;

        public AdmissionLease(SseAdmissionGate gate) => _gate = gate;

        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }
    }
}
