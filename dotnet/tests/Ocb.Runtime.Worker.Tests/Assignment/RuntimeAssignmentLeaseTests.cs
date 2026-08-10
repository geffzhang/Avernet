using System.Diagnostics.CodeAnalysis;
using Ocb.GrainContracts.Runtime;
using Ocb.Runtime.Worker.Application.Assignment;

namespace Ocb.Runtime.Worker.Tests.Assignment;

/// <summary>
/// Verify lease and reassign DTO semantics: expiration, token
/// uniqueness, and accepted/rejected states.
/// Also verify the RuntimeAssignmentCoordinator business logic.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class RuntimeAssignmentLeaseTests
{
    private readonly RuntimeAssignmentCoordinator _coordinator;

    public RuntimeAssignmentLeaseTests()
    {
        _coordinator = new RuntimeAssignmentCoordinator();
    }

    #region DTO Semantics

    [Fact]
    public void LeaseExpiresAt_MustBeAfterIssuedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var future = now.AddMinutes(5);

        var lease = new WorkerAssignmentLease(
            "token-123", "bot-1", "worker-a", now, future);

        Assert.True(lease.ExpiresAt > lease.IssuedAt,
            "Lease expiry must be after issue time.");
    }

    [Fact]
    public void ReassignResult_Accepted_IncludesLeaseToken()
    {
        var result = new ReassignResult(Accepted: true, LeaseToken: "token-456", Message: null);

        Assert.True(result.Accepted);
        Assert.NotNull(result.LeaseToken);
        Assert.Null(result.Message);
    }

    [Fact]
    public void ReassignResult_Rejected_IncludesMessage()
    {
        var result = new ReassignResult(Accepted: false, LeaseToken: null, Message: "Bot is pinned");

        Assert.False(result.Accepted);
        Assert.Null(result.LeaseToken);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void ReassignRequest_ContainsRequiredFields()
    {
        var request = new RuntimeReassignRequest(
            "tenant-1", "subj-1", "bot-abc", "load-balance", "worker-1");

        Assert.Equal("tenant-1", request.TenantId);
        Assert.Equal("bot-abc", request.BotId);
        Assert.Equal("load-balance", request.Reason);
        Assert.Equal("worker-1", request.RequestedByWorkerId);
    }

    [Fact]
    public void LeaseRenewalRequest_CarriesObservedState()
    {
        var observed = new ObservedRuntimeState(
            "bot-1", "worker-a", "running", 1234, 8080, "/health", DateTimeOffset.UtcNow);

        var renewal = new LeaseRenewalRequest("token-abc", "bot-1", "worker-a", observed);

        Assert.NotNull(renewal.CurrentObserved);
        Assert.Equal(1234, renewal.CurrentObserved!.Pid);
    }

    #endregion

    #region Coordinator — Assignment

    [Fact]
    public async Task AssignWorker_ShouldIssueLease()
    {
        var request = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);

        var result = await _coordinator.AssignWorkerAsync(request, default);

        Assert.True(result.Accepted);
        Assert.NotNull(result.Lease);
        Assert.Equal("bot-1", result.Lease!.BotId);
        Assert.Equal("worker-a", result.Lease.WorkerId);
        Assert.NotNull(result.Lease.LeaseToken);
    }

    [Fact]
    public async Task AssignWorker_WhenConflict_ShouldReject()
    {
        var first = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);

        await _coordinator.AssignWorkerAsync(first, default);

        var second = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-b", LeaseDurationMinutes: 5);

        var result = await _coordinator.AssignWorkerAsync(second, default);

        Assert.False(result.Accepted);
        Assert.Equal("LEASE_CONFLICT", result.Reason);
    }

    #endregion

    #region Coordinator — Renew

    [Fact]
    public async Task RenewLease_WhenValid_ShouldSucceed()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        var assigned = await _coordinator.AssignWorkerAsync(assign, default);

        var renew = new RenewLeaseRequest(
            "tenant-1", "bot-1",
            assigned.Lease!.LeaseToken,
            assigned.Lease.ExpiresAt);

        var result = await _coordinator.RenewLeaseAsync(renew, default);

        Assert.True(result.Accepted);
        Assert.NotNull(result.NewExpiresAt);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task RenewLease_WhenExpired_ShouldReturnRejected()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        var assigned = await _coordinator.AssignWorkerAsync(assign, default);

        // Pass an already-expired time
        var renew = new RenewLeaseRequest(
            "tenant-1", "bot-1",
            assigned.Lease!.LeaseToken,
            LeaseExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _coordinator.RenewLeaseAsync(renew, default);

        Assert.False(result.Accepted);
        Assert.Equal("LEASE_EXPIRED", result.Reason);
    }

    [Fact]
    public async Task RenewLease_WithWrongToken_ShouldReject()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        var assigned = await _coordinator.AssignWorkerAsync(assign, default);

        var renew = new RenewLeaseRequest(
            "tenant-1", "bot-1",
            LeaseToken: "wrong-token",
            assigned.Lease!.ExpiresAt);

        var result = await _coordinator.RenewLeaseAsync(renew, default);

        Assert.False(result.Accepted);
        Assert.Equal("LEASE_TOKEN_MISMATCH", result.Reason);
    }

    [Fact]
    public async Task RenewLease_NotFound_ShouldReject()
    {
        var renew = new RenewLeaseRequest(
            "tenant-1", "nonexistent", "token-abc",
            LeaseExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await _coordinator.RenewLeaseAsync(renew, default);

        Assert.False(result.Accepted);
        Assert.Equal("LEASE_NOT_FOUND", result.Reason);
    }

    #endregion

    #region Coordinator — Release

    [Fact]
    public async Task ReleaseLease_WhenValid_ShouldSucceed()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        var assigned = await _coordinator.AssignWorkerAsync(assign, default);

        var release = new ReleaseLeaseRequest(
            "tenant-1", "bot-1", assigned.Lease!.LeaseToken);

        var result = await _coordinator.ReleaseLeaseAsync(release, default);

        Assert.True(result.Released);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task ReleaseLease_WithWrongToken_ShouldReject()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        await _coordinator.AssignWorkerAsync(assign, default);

        var release = new ReleaseLeaseRequest(
            "tenant-1", "bot-1", LeaseToken: "wrong-token");

        var result = await _coordinator.ReleaseLeaseAsync(release, default);

        Assert.False(result.Released);
        Assert.Equal("LEASE_TOKEN_MISMATCH", result.Reason);
    }

    [Fact]
    public async Task ReleaseLease_ThenReassign_ShouldSucceed()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        var assigned = await _coordinator.AssignWorkerAsync(assign, default);

        var release = new ReleaseLeaseRequest(
            "tenant-1", "bot-1", assigned.Lease!.LeaseToken);
        await _coordinator.ReleaseLeaseAsync(release, default);

        // After release, a new assignment should succeed
        var reassign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-b", LeaseDurationMinutes: 5);

        var result = await _coordinator.AssignWorkerAsync(reassign, default);

        Assert.True(result.Accepted);
        Assert.Equal("worker-b", result.Lease!.WorkerId);
    }

    #endregion

    #region Coordinator — Reassign

    [Fact]
    public async Task Reassign_ShouldCreateNewToken()
    {
        var assign = new AssignWorkerRequest(
            "tenant-1", "bot-1", "worker-a", LeaseDurationMinutes: 5);
        var assigned = await _coordinator.AssignWorkerAsync(assign, default);
        var oldToken = assigned.Lease!.LeaseToken;

        var reassign = new RuntimeReassignRequest(
            "tenant-1", "subj-1", "bot-1", "load-balance", "worker-2");

        var result = await _coordinator.RequestInternalReassignAsync(reassign, default);

        Assert.True(result.Accepted);
        Assert.NotNull(result.LeaseToken);
        Assert.NotEqual(oldToken, result.LeaseToken);
    }

    #endregion
}
