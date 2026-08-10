namespace Ocb.GrainContracts.Runtime;

/// <summary>
/// Request to assign a worker to a bot.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.AssignWorkerRequest")]
public sealed record AssignWorkerRequest(
    [property: Id(0)] string TenantId,
    [property: Id(1)] string BotId,
    [property: Id(2)] string WorkerId,
    [property: Id(3)] int LeaseDurationMinutes);

/// <summary>
/// Result of a worker assignment attempt.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.AssignmentResult")]
public sealed record AssignmentResult(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] WorkerAssignmentLease? Lease,
    [property: Id(2)] string? Reason);

/// <summary>
/// Request to renew an existing lease.
/// Parameterised record with expiresAt for testability.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.RenewLeaseRequest")]
public sealed record RenewLeaseRequest(
    [property: Id(0)] string TenantId,
    [property: Id(1)] string BotId,
    [property: Id(2)] string LeaseToken,
    [property: Id(3)] DateTimeOffset LeaseExpiresAtUtc);

/// <summary>
/// Result of a lease renewal attempt.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.LeaseRenewResult")]
public sealed record LeaseRenewResult(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] string? Reason,
    [property: Id(2)] DateTimeOffset? NewExpiresAt);

/// <summary>
/// Request to release a lease.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.ReleaseLeaseRequest")]
public sealed record ReleaseLeaseRequest(
    [property: Id(0)] string TenantId,
    [property: Id(1)] string BotId,
    [property: Id(2)] string LeaseToken);

/// <summary>
/// Result of a lease release.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.LeaseReleaseResult")]
public sealed record LeaseReleaseResult(
    [property: Id(0)] bool Released,
    [property: Id(1)] string? Reason);
