using Ocb.Contracts.Workspaces;

namespace Ocb.Runtime.Worker.Application.Recovery;

/// <summary>
/// Scans and cleans up orphaned workspace directories.
/// A workspace is considered orphaned when its directory exists
/// but no active handle or lease references it.
/// </summary>
public sealed class OrphanRecoveryService : IOrphanRecoveryService
{
    private readonly string _workspaceRoot;

    public OrphanRecoveryService(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public Task<IReadOnlyList<RecoveredOrphan>> RecoverOrphansAsync(
        CancellationToken ct)
    {
        if (!Directory.Exists(_workspaceRoot))
            return Task.FromResult<IReadOnlyList<RecoveredOrphan>>(Array.Empty<RecoveredOrphan>());

        var recovered = new List<RecoveredOrphan>();

        foreach (var dir in Directory.EnumerateDirectories(_workspaceRoot))
        {
            ct.ThrowIfCancellationRequested();

            if (IsOrphan(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    recovered.Add(new RecoveredOrphan(
                        Path.GetFileName(dir),
                        "LEASE_MISSING_OR_EXPIRED"));
                }
                catch (IOException)
                {
                    // Directory locked by another process — skip
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RecoveredOrphan>>(recovered);
    }

    /// <summary>
    /// Determines if a workspace directory is orphaned.
    /// A directory is orphaned if no lease marker file exists.
    /// </summary>
    private static bool IsOrphan(string directoryPath)
    {
        var leaseFile = Path.Combine(directoryPath, ".lease");
        if (!File.Exists(leaseFile))
            return true;

        // Read lease file and check expiry
        try
        {
            var content = File.ReadAllText(leaseFile).Trim();
            if (DateTimeOffset.TryParse(content, out var expiresAt))
                return expiresAt <= DateTimeOffset.UtcNow;
        }
        catch
        {
            // Unreadable lease file = orphan
            return true;
        }

        return false;
    }
}
