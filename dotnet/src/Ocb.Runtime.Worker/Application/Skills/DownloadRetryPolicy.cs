namespace Ocb.Runtime.Worker.Application.Skills;

/// <summary>
/// Exponential backoff retry policy for skill downloads.
/// </summary>
public sealed class DownloadRetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public DownloadRetryPolicy(
        int maxRetries = 3,
        int baseDelayMs = 500,
        int maxDelayMs = 10000)
    {
        _maxRetries = maxRetries;
        _baseDelay = TimeSpan.FromMilliseconds(baseDelayMs);
        _maxDelay = TimeSpan.FromMilliseconds(maxDelayMs);
    }

    public int MaxRetries => _maxRetries;

    public TimeSpan GetDelay(int attempt)
    {
        var delay = TimeSpan.FromMilliseconds(
            _baseDelay.TotalMilliseconds * Math.Pow(2, attempt));

        return delay > _maxDelay ? _maxDelay : delay;
    }
}
