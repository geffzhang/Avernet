namespace Ocb.Runtime.Worker.Application.Skills;

/// <summary>
/// Publishes the materialized skill set atomically to the bot process.
/// Ensures that a partial materialization is never observed.
/// </summary>
public sealed class AtomicActivationPublisher
{
    /// <summary>
    /// Publishes an atomic activation view. The implementation
    /// atomically swaps the active skills directory via a symlink
    /// or rename operation.
    ///
    /// Currently stubbed — full filesystem swap is deferred until
    /// the skill registry backend is available.
    /// </summary>
    public Task PublishAsync(
        string activeViewDir,
        string stagedViewDir,
        CancellationToken ct)
    {
        // Stub: mark as published
        return Task.CompletedTask;
    }
}
