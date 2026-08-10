namespace Ocb.Contracts.Workspaces;

/// <summary>
/// Request to prepare an isolated workspace.
/// </summary>
public sealed record WorkspaceRequest(
    string BotId,
    string WorkerId,
    string WorkspaceRoot);

/// <summary>
/// Handle to an active workspace — tracks the directory path
/// and creation time for lifecycle management.
/// </summary>
public sealed record WorkspaceHandle(
    string WorkspaceId,
    string Path,
    string BotId,
    DateTimeOffset CreatedAt);

/// <summary>
/// A recovered orphan workspace — found and cleaned up by
/// the orphan recovery service.
/// </summary>
public sealed record RecoveredOrphan(
    string WorkspaceId,
    string Reason);
