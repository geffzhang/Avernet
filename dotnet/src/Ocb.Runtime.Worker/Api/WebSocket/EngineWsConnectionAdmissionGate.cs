namespace Ocb.Runtime.Worker.Api.WebSocket;

/// <summary>
/// Connection admission control for the engine WebSocket endpoint.
/// Mirrors Python ws_server.py ConnectionLimiter — rejects connections
/// above MaxConnections with HTTP 503 + Retry-After.
/// </summary>
public sealed class EngineWsConnectionAdmissionGate
{
    private readonly int _maxConnections;
    private int _activeConnections;

    public EngineWsConnectionAdmissionGate(int maxConnections)
    {
        _maxConnections = maxConnections;
    }

    /// <summary>
    /// True when the gate is at capacity — new connections should be rejected.
    /// </summary>
    public bool AtCapacity => _activeConnections >= _maxConnections;

    /// <summary>
    /// Attempts to acquire a connection slot. Returns true if acquired,
    /// false if at capacity.
    /// </summary>
    public bool TryAcquire()
    {
        if (Interlocked.Increment(ref _activeConnections) > _maxConnections)
        {
            Interlocked.Decrement(ref _activeConnections);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Releases a connection slot when the WebSocket is closed.
    /// </summary>
    public void Release() => Interlocked.Decrement(ref _activeConnections);

    /// <summary>
    /// Current number of active connections (for diagnostics).
    /// </summary>
    public int ActiveConnections => Volatile.Read(ref _activeConnections);
}
