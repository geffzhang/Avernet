using Ocb.Contracts.Workspaces;

namespace Ocb.Runtime.Worker.Application.Workspaces;

/// <summary>
/// Contract for workspace isolation — creates and cleans up
/// isolated directories for bot processes.
/// </summary>
public interface IWorkspaceIsolationService
{
    /// <summary>
    /// Prepares an isolated workspace directory for a bot process.
    /// Creates the directory if it does not exist and returns a handle
    /// that tracks the workspace lifecycle.
    /// </summary>
    Task<WorkspaceHandle> PrepareWorkspaceAsync(
        WorkspaceRequest request, CancellationToken ct);

    /// <summary>
    /// Cleans up a workspace by deleting its directory tree.
    /// Idempotent — safe to call on already-cleaned workspaces.
    /// </summary>
    Task CleanupWorkspaceAsync(
        WorkspaceHandle handle, CancellationToken ct);
}
