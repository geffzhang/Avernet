using Ocb.Contracts.Workspaces;

namespace Ocb.Runtime.Worker.Application.Workspaces;

/// <summary>
/// Manages isolated workspace directories for bot processes.
/// Creates per-bot directories under a workspace root and
/// tracks active handles for cleanup.
/// </summary>
public sealed class WorkspaceIsolationService : IWorkspaceIsolationService
{
    private readonly string _workspaceRoot;
    private readonly Dictionary<string, WorkspaceHandle> _active = new();
    private readonly object _lock = new();

    public WorkspaceIsolationService(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public Task<WorkspaceHandle> PrepareWorkspaceAsync(
        WorkspaceRequest request, CancellationToken ct)
    {
        var workspaceId = GenerateWorkspaceId(request.BotId, request.WorkerId);

        lock (_lock)
        {
            if (_active.TryGetValue(workspaceId, out var existing))
                return Task.FromResult(existing);
        }

        var path = System.IO.Path.Combine(_workspaceRoot, workspaceId);
        Directory.CreateDirectory(path);

        var handle = new WorkspaceHandle(
            workspaceId, path, request.BotId,
            DateTimeOffset.UtcNow);

        lock (_lock) { _active[workspaceId] = handle; }

        return Task.FromResult(handle);
    }

    public Task CleanupWorkspaceAsync(
        WorkspaceHandle handle, CancellationToken ct)
    {
        lock (_lock) { _active.Remove(handle.WorkspaceId); }

        if (Directory.Exists(handle.Path))
            Directory.Delete(handle.Path, recursive: true);

        return Task.CompletedTask;
    }

    private static string GenerateWorkspaceId(string botId, string workerId)
    {
        var input = $"{botId}:{workerId}";
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return $"ws-{Convert.ToHexStringLower(hash)[..12]}";
    }
}
