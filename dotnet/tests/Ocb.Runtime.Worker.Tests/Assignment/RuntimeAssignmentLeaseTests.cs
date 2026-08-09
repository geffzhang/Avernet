using System.Diagnostics.CodeAnalysis;
using Ocb.GrainContracts.Runtime;

namespace Ocb.Runtime.Worker.Tests.Assignment;

/// <summary>
/// Verify lease and reassign DTO semantics: expiration, token
/// uniqueness, and accepted/rejected states.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class RuntimeAssignmentLeaseTests
{
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
}
