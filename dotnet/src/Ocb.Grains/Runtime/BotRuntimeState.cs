using Ocb.GrainContracts.Runtime;

namespace Ocb.Grains.Runtime;

/// <summary>
/// Persistent state for a BotRuntimeGrain.
/// Contains only serializable fields — no process handles, file paths,
/// or OS-level resources.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.Grains.Runtime.BotRuntimeState")]
public sealed class BotRuntimeState
{
    [Id(0)]
    public DesiredRuntimeState? Desired { get; set; }

    [Id(1)]
    public ObservedRuntimeState? Observed { get; set; }

    /// <summary>
    /// Tracks whether a reassign was scheduled due to worker loss.
    /// Cleared when a new observed state is reported.
    /// </summary>
    [Id(2)]
    public bool ReassignPending { get; set; }

    /// <summary>
    /// The worker ID that triggered the last reassign.
    /// </summary>
    [Id(3)]
    public string? LastLostWorkerId { get; set; }
}
