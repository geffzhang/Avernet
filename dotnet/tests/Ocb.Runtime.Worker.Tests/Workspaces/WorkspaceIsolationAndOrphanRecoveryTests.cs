using System.Diagnostics.CodeAnalysis;
using Ocb.Contracts.Workspaces;
using Ocb.Runtime.Worker.Application.Recovery;
using Ocb.Runtime.Worker.Application.Workspaces;

namespace Ocb.Runtime.Worker.Tests.Workspaces;

/// <summary>
/// Tests for workspace isolation, idempotent preparation,
/// cleanup, and orphan recovery.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class WorkspaceIsolationAndOrphanRecoveryTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceIsolationService _isolation;
    private readonly OrphanRecoveryService _recovery;

    public WorkspaceIsolationAndOrphanRecoveryTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocb-ws-test-{Guid.NewGuid():N}");
        _isolation = new WorkspaceIsolationService(_workspaceRoot);
        _recovery = new OrphanRecoveryService(_workspaceRoot);
    }

    #region Workspace Isolation

    [Fact]
    public async Task PrepareWorkspace_ShouldCreateDirectory()
    {
        var request = new WorkspaceRequest("bot-1", "worker-a", _workspaceRoot);

        var handle = await _isolation.PrepareWorkspaceAsync(request, default);

        Assert.NotNull(handle);
        Assert.True(Directory.Exists(handle.Path));
        Assert.Equal("bot-1", handle.BotId);
    }

    [Fact]
    public async Task PrepareWorkspace_SameId_ShouldReturnExistingHandle()
    {
        var request = new WorkspaceRequest("bot-1", "worker-a", _workspaceRoot);

        var first = await _isolation.PrepareWorkspaceAsync(request, default);
        var second = await _isolation.PrepareWorkspaceAsync(request, default);

        Assert.Equal(first.WorkspaceId, second.WorkspaceId);
        Assert.Equal(first.Path, second.Path);
    }

    [Fact]
    public async Task CleanupWorkspace_ShouldRemoveDirectory()
    {
        var request = new WorkspaceRequest("bot-2", "worker-b", _workspaceRoot);
        var handle = await _isolation.PrepareWorkspaceAsync(request, default);

        Assert.True(Directory.Exists(handle.Path));

        await _isolation.CleanupWorkspaceAsync(handle, default);

        Assert.False(Directory.Exists(handle.Path));
    }

    [Fact]
    public async Task CleanupWorkspace_Idempotent_NoException()
    {
        var request = new WorkspaceRequest("bot-3", "worker-c", _workspaceRoot);
        var handle = await _isolation.PrepareWorkspaceAsync(request, default);

        await _isolation.CleanupWorkspaceAsync(handle, default);
        // Second cleanup should not throw
        await _isolation.CleanupWorkspaceAsync(handle, default);
    }

    [Fact]
    public async Task PrepareWorkspace_DifferentBots_HaveDifferentPaths()
    {
        var reqA = new WorkspaceRequest("bot-a", "w1", _workspaceRoot);
        var reqB = new WorkspaceRequest("bot-b", "w1", _workspaceRoot);

        var handleA = await _isolation.PrepareWorkspaceAsync(reqA, default);
        var handleB = await _isolation.PrepareWorkspaceAsync(reqB, default);

        Assert.NotEqual(handleA.Path, handleB.Path);
        Assert.NotEqual(handleA.WorkspaceId, handleB.WorkspaceId);
    }

    #endregion

    #region Orphan Recovery

    [Fact]
    public async Task RecoverOrphans_NoOrphans_ReturnsEmpty()
    {
        var recovered = await _recovery.RecoverOrphansAsync(default);
        Assert.Empty(recovered);
    }

    [Fact]
    public async Task RecoverOrphans_ShouldRemoveExpiredWorkspaceAndReportRecovery()
    {
        // Create an orphan workspace directory (no .lease file)
        var orphanPath = Path.Combine(_workspaceRoot, "ws-orphan-1");
        Directory.CreateDirectory(orphanPath);

        var recovered = await _recovery.RecoverOrphansAsync(default);

        Assert.Contains(recovered, x => x.WorkspaceId == "ws-orphan-1");
        Assert.Equal("LEASE_MISSING_OR_EXPIRED", recovered[0].Reason);
        Assert.False(Directory.Exists(orphanPath));
    }

    [Fact]
    public async Task RecoverOrphans_ValidLeaseNotRemoved()
    {
        // Create a workspace with a valid (future) lease
        var validPath = Path.Combine(_workspaceRoot, "ws-valid");
        Directory.CreateDirectory(validPath);
        File.WriteAllText(
            Path.Combine(validPath, ".lease"),
            DateTimeOffset.UtcNow.AddHours(1).ToString("O"));

        var recovered = await _recovery.RecoverOrphansAsync(default);

        Assert.DoesNotContain(recovered, x => x.WorkspaceId == "ws-valid");
        Assert.True(Directory.Exists(validPath));
    }

    [Fact]
    public async Task RecoverOrphans_ExpiredLeaseRemoved()
    {
        var expiredPath = Path.Combine(_workspaceRoot, "ws-expired");
        Directory.CreateDirectory(expiredPath);
        File.WriteAllText(
            Path.Combine(expiredPath, ".lease"),
            DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));

        var recovered = await _recovery.RecoverOrphansAsync(default);

        Assert.Contains(recovered, x => x.WorkspaceId == "ws-expired");
        Assert.False(Directory.Exists(expiredPath));
    }

    #endregion

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }
}
