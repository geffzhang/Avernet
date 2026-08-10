using Ocb.Contracts.Fusion;

namespace Ocb.GrainContracts.Fusion;

/// <summary>
/// Persistent state for <see cref="IFusionJobGrain"/>. Stores idempotency
/// records and execution metadata. Never contains framework objects
/// (HttpClient, IVectorStore, SqlConnection, WebSocket, etc.).
/// </summary>
[Alias("Ocb.GrainContracts.Fusion.FusionJobState")]
[GenerateSerializer]
public sealed class FusionJobState
{
    /// <summary>
    /// Completed idempotency keys mapped to their fusion results.
    /// </summary>
    [Id(0)]
    public Dictionary<string, FuseResponseDto> CompletedByIdempotency { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Total number of distinct executions that have run on this grain.
    /// </summary>
    [Id(1)]
    public int ExecutionCount { get; set; }

    /// <summary>
    /// The fusion ID of the most recent execution.
    /// </summary>
    [Id(2)]
    public string? LastFusionId { get; set; }

    /// <summary>
    /// Timestamp when this grain state was first created.
    /// </summary>
    [Id(3)]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
