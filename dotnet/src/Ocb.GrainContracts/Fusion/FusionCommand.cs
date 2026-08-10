using Ocb.Contracts;
using Ocb.Contracts.Fusion;

namespace Ocb.GrainContracts.Fusion;

/// <summary>
/// Command payload for <see cref="IFusionJobGrain.ExecuteAsync"/>.
/// Carries the caller identity, idempotency key, and fusion request.
/// </summary>
[Alias("Ocb.GrainContracts.Fusion.FusionCommand")]
[GenerateSerializer]
public sealed record FusionCommand(
    [property: Id(0)] string IdempotencyKey,
    [property: Id(1)] CallerContext Caller,
    [property: Id(2)] FusionRequestDto Request
);
