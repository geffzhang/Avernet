using Ocb.Contracts.Workspaces;

namespace Ocb.Runtime.Worker.Application.Recovery;

/// <summary>
/// Contract for orphan workspace recovery — finds and cleans up
/// workspace directories that have no active lease or process.
/// </summary>
public interface IOrphanRecoveryService
{
    /// <summary>
    /// Scans the workspace root for orphaned directories and removes them.
    /// Returns the list of recovered orphans.
    /// </summary>
    Task<IReadOnlyList<RecoveredOrphan>> RecoverOrphansAsync(
        CancellationToken ct);
}
