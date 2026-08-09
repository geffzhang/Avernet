namespace Ocb.GrainContracts.Runtime;

/// <summary>
/// Internal reassign request — used between coordinator and worker.
/// NOT exposed as a public API endpoint.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.RuntimeReassignRequest")]
public sealed record RuntimeReassignRequest(
    [property: Id(0)] string TenantId,
    [property: Id(1)] string SubjectId,
    [property: Id(2)] string BotId,
    [property: Id(3)] string Reason,
    [property: Id(4)] string RequestedByWorkerId);

/// <summary>
/// Result of an internal reassign attempt.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.ReassignResult")]
public sealed record ReassignResult(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] string? LeaseToken,
    [property: Id(2)] string? Message);

/// <summary>
/// Lease token issued by the coordinator when a worker acquires a bot assignment.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.WorkerAssignmentLease")]
public sealed record WorkerAssignmentLease(
    [property: Id(0)] string LeaseToken,
    [property: Id(1)] string BotId,
    [property: Id(2)] string WorkerId,
    [property: Id(3)] DateTimeOffset IssuedAt,
    [property: Id(4)] DateTimeOffset ExpiresAt);

/// <summary>
/// Worker heartbeat/lease renewal request.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Runtime.LeaseRenewalRequest")]
public sealed record LeaseRenewalRequest(
    [property: Id(0)] string LeaseToken,
    [property: Id(1)] string BotId,
    [property: Id(2)] string WorkerId,
    [property: Id(3)] ObservedRuntimeState? CurrentObserved);
