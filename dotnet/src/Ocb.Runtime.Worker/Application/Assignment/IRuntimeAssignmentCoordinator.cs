using Ocb.GrainContracts.Runtime;

namespace Ocb.Runtime.Worker.Application.Assignment;

/// <summary>
/// Internal coordination interface for bot reassignment.
/// This is NOT a public API endpoint — it is used between workers
/// and the coordinator for internal load-balancing and failover.
/// </summary>
public interface IRuntimeAssignmentCoordinator
{
    /// <summary>
    /// Request that a bot be reassigned to a different worker.
    /// Only available internally; never exposed as a public HTTP/WS method.
    /// </summary>
    Task<ReassignResult> RequestInternalReassignAsync(
        RuntimeReassignRequest request,
        CancellationToken ct);
}
