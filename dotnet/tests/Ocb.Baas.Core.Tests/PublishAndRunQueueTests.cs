using System.Diagnostics.CodeAnalysis;
using Ocb.Baas.Core.Common;
using Ocb.Baas.Core.Publish;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Publish;
using Ocb.PluginApi.Baas.Persistence;

namespace Ocb.Baas.Core.Tests;

/// <summary>
/// Verify publish lifecycle state machine and run queue operations.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PublishAndRunQueueTests
{
    private static CallerContext Caller => new("tenant-a", "u-1", new HashSet<string> { "user" });

    private static (PublishService svc, InMemoryPublishRepository repo) CreateService()
    {
        var repo = new InMemoryPublishRepository();
        return (new PublishService(repo), repo);
    }

    [Fact]
    public async Task CreatePublish_ShouldSetStatus_Pending()
    {
        var (svc, _) = CreateService();
        var result = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        Assert.Equal(PublishStatus.Pending, result.Status);
        Assert.Equal("bot-1", result.BotId);
        Assert.Equal("1.0.0", result.Version);
    }

    [Fact]
    public async Task Approve_ShouldTransition_Pending_To_Reviewing()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        var approved = await svc.ApproveStageAsync(Caller, created.Id, "operator-a");
        Assert.Equal(PublishStatus.Reviewing, approved.Status);
    }

    [Fact]
    public async Task Approve_ShouldTransition_Reviewing_To_Active()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        // First approve: pending → reviewing
        await svc.ApproveStageAsync(Caller, created.Id, "operator-a");

        // Second approve: reviewing → active
        var active = await svc.ApproveStageAsync(Caller, created.Id, "operator-b");
        Assert.Equal(PublishStatus.Active, active.Status);
    }

    [Fact]
    public async Task Reject_FromPending_ShouldSetRejected()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        var rejected = await svc.RejectPublishAsync(Caller, created.Id, "Not needed");
        Assert.Equal(PublishStatus.Rejected, rejected.Status);
    }

    [Fact]
    public async Task Reject_FromActive_ShouldThrow()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));
        await svc.ApproveStageAsync(Caller, created.Id, "op");
        await svc.ApproveStageAsync(Caller, created.Id, "op");

        await Assert.ThrowsAsync<DomainConflictException>(
            () => svc.RejectPublishAsync(Caller, created.Id, "too late"));
    }

    [Fact]
    public async Task Retry_OnlyWorks_OnFailed()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        // Retry on pending should throw
        await Assert.ThrowsAsync<DomainConflictException>(
            () => svc.RetryPublishAsync(Caller, created.Id));
    }

    [Fact]
    public async Task Complete_ShouldSetCompleted()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        var completed = await svc.CompletePublishAsync(Caller, created.Id);
        Assert.Equal(PublishStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Revoke_ShouldSetRevoked()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        var revoked = await svc.RevokePublishAsync(Caller, created.Id, "security issue");
        Assert.Equal(PublishStatus.Revoked, revoked.Status);
        Assert.Equal("security issue", revoked.ErrorMessage);
    }

    [Fact]
    public async Task ListPublishes_ShouldBeTenantScoped()
    {
        var (svc, _) = CreateService();
        await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        var list = await svc.ListPublishesAsync(Caller);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetPublish_WhenTenantMismatch_ShouldThrow()
    {
        var (svc, _) = CreateService();
        var created = await svc.CreatePublishAsync(Caller,
            new CreatePublishRequest("bot-1", "1.0.0", "git://repo/skills", null));

        var otherCaller = new CallerContext("tenant-b", "u-2", new HashSet<string> { "user" });
        await Assert.ThrowsAsync<TenantMismatchException>(
            () => svc.GetPublishAsync(otherCaller, created.Id));
    }
}

internal sealed class InMemoryPublishRepository : IPublishRepository
{
    private readonly Dictionary<long, PublishRecord> _store = new();
    private long _nextId = 1;

    public Task<PublishRecord?> GetByIdAsync(long publishId, CancellationToken ct)
    {
        _store.TryGetValue(publishId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<PublishRecord>> ListByTenantAsync(string tenantId, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<PublishRecord>>(
            _store.Values.Where(r => r.TenantId == tenantId).ToList());
    }

    public Task<long> InsertAsync(PublishRecord record, CancellationToken ct)
    {
        var id = _nextId++;
        _store[id] = record with { Id = id };
        return Task.FromResult(id);
    }

    public Task UpdateAsync(PublishRecord record, CancellationToken ct)
    {
        _store[record.Id] = record;
        return Task.CompletedTask;
    }
}
