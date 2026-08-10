namespace Ocb.PluginApi.Baas.Queue;

/// <summary>
/// Provider interface for bot run queue operations — enqueue, lease, acknowledge.
/// </summary>
public interface IBotRunQueueProvider
{
    /// <summary>
    /// Enqueues a bot run item for execution.
    /// </summary>
    Task<BotRunQueueEntry> EnqueueAsync(BotRunQueueItem item, CancellationToken ct = default);

    /// <summary>
    /// Acquires a lease on the next available queue item.
    /// Returns null if no items are available.
    /// </summary>
    Task<BotRunQueueLease?> LeaseAsync(string workerId, CancellationToken ct = default);

    /// <summary>
    /// Acknowledges successful completion of a leased item.
    /// </summary>
    Task AckAsync(long entryId, string workerId, CancellationToken ct = default);

    /// <summary>
    /// Moves an item to the dead-letter queue after max retries.
    /// </summary>
    Task DeadLetterAsync(long entryId, string reason, CancellationToken ct = default);
}

public sealed record BotRunQueueItem(
    string BotId,
    string RunType,
    string? Payload,
    int Priority,
    int MaxRetries);

public sealed record BotRunQueueEntry(
    long Id,
    string BotId,
    string Status,
    int Attempt,
    DateTimeOffset CreatedAt);

public sealed record BotRunQueueLease(
    long EntryId,
    string BotId,
    string WorkerId,
    string LeaseToken,
    DateTimeOffset LeasedAt,
    DateTimeOffset ExpiresAt);
