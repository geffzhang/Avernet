namespace Ocb.Channels.WebSocket;

/// <summary>
/// Sliding-window rate limiter that enforces a maximum number of messages
/// per second per connection. Refuses messages when the window is full
/// so the session can close with an appropriate error code.
/// </summary>
public sealed class ConnectionRateLimiter(int messagesPerSecond)
{
    private readonly Queue<long> _timestamps = new();

    /// <summary>
    /// Returns true when the message at <paramref name="unixMillis"/> is
    /// within the per-second budget.
    /// </summary>
    public bool TryAccept(long unixMillis)
    {
        // Evict timestamps older than 1 second.
        while (_timestamps.Count > 0 && unixMillis - _timestamps.Peek() >= 1000)
            _timestamps.Dequeue();

        if (_timestamps.Count >= messagesPerSecond)
            return false;

        _timestamps.Enqueue(unixMillis);
        return true;
    }
}
