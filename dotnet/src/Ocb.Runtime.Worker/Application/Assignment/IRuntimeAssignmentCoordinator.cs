using Ocb.GrainContracts.Runtime;

namespace Ocb.Runtime.Worker.Application.Assignment;

/// <summary>
/// Internal coordination interface for bot assignment, lease lifecycle,
/// and internal reassign.
/// This is NOT a public API endpoint — it is used between workers
/// and the coordinator for internal load-balancing and failover.
/// </summary>
public interface IRuntimeAssignmentCoordinator
{
    /// <summary>
    /// Request that a bot be assigned to a worker.
    /// </summary>
    Task<AssignmentResult> AssignWorkerAsync(
        AssignWorkerRequest request,
        CancellationToken ct);

    /// <summary>
    /// Renew an existing lease for a bot assignment.
    /// </summary>
    Task<LeaseRenewResult> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken ct);

    /// <summary>
    /// Release a lease, freeing the bot for reassignment.
    /// </summary>
    Task<LeaseReleaseResult> ReleaseLeaseAsync(
        ReleaseLeaseRequest request,
        CancellationToken ct);

    /// <summary>
    /// Request that a bot be reassigned to a different worker.
    /// Only available internally; never exposed as a public HTTP/WS method.
    /// </summary>
    Task<ReassignResult> RequestInternalReassignAsync(
        RuntimeReassignRequest request,
        CancellationToken ct);
}
