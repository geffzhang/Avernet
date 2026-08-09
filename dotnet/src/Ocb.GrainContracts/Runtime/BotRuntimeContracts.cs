using Orleans;

namespace Ocb.GrainContracts.Runtime;

/// <summary>
/// Grain that owns the authoritative desired/observed state for a bot runtime.
/// The state is serializable — no process handles or local paths.
/// </summary>
[Alias("Ocb.GrainContracts.Runtime.IBotRuntimeGrain")]
public interface IBotRuntimeGrain : IGrainWithStringKey
{
    /// <summary>
    /// Report the current observed state from the worker that owns this bot.
    /// </summary>
    Task ReportObservedAsync(ObservedRuntimeState observed);

    /// <summary>
    /// Set the desired state (called by the coordinator).
    /// </summary>
    Task SetDesiredAsync(DesiredRuntimeState desired);

    /// <summary>
    /// Read the current desired + observed snapshot.
    /// </summary>
    Task<BotRuntimeSnapshot> GetSnapshotAsync();
}

/// <summary>
/// What the coordinator wants the bot to become.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.DesiredRuntimeState")]
public sealed record DesiredRuntimeState(
    [property: Id(0)] string BotId,
    [property: Id(1)] string? WorkerId,
    [property: Id(2)] string? Status,
    [property: Id(3)] string? Version);

/// <summary>
/// What the worker reports the bot actually is right now.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.ObservedRuntimeState")]
public sealed record ObservedRuntimeState(
    [property: Id(0)] string BotId,
    [property: Id(1)] string WorkerId,
    [property: Id(2)] string Status,
    [property: Id(3)] int? Pid,
    [property: Id(4)] int? Port,
    [property: Id(5)] string? HealthEndpoint,
    [property: Id(6)] DateTimeOffset ObservedAt);

/// <summary>
/// Combined desired + observed snapshot.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.BotRuntimeSnapshot")]
public sealed record BotRuntimeSnapshot(
    [property: Id(0)] DesiredRuntimeState? Desired,
    [property: Id(1)] ObservedRuntimeState? Observed);
