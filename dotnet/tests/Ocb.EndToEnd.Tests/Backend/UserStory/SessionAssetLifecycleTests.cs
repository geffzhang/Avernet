using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Assets;
using Ocb.Contracts.Resources;

namespace Ocb.EndToEnd.Tests.Backend.UserStory;

/// <summary>
/// User story: a session is created, assets are uploaded and recorded, and if
/// the persistence operation fails, the temp object is recorded for cleanup.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SessionAssetLifecycleTests
{
    [Fact]
    public async Task ExecuteWithCompensation_OnSuccess_CompletesWithoutRecording()
    {
        var repo = new FakeCompensationRepo();
        var service = new AssetCompensationService(repo);
        var invoked = false;

        await service.ExecuteWithCompensationAsync("t1", "tenant/t1/key",
            _ => { invoked = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(invoked);
        Assert.Empty(repo.Recorded);
    }

    [Fact]
    public async Task ExecuteWithCompensation_OnFailure_RecordsAndRethrows()
    {
        var repo = new FakeCompensationRepo();
        var service = new AssetCompensationService(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteWithCompensationAsync("t1", "tenant/t1/key",
                _ => throw new InvalidOperationException("DB failure"),
                CancellationToken.None));

        var record = Assert.Single(repo.Recorded);
        Assert.Equal("TEMP_UPLOADED", record.State);
    }

    [Fact]
    public void SessionAssetRecord_ContainsRequiredFields()
    {
        var record = new SessionAssetRecord(
            "tenant-a", "bot-1", "session-1", "res-1",
            "tenant/t1/bots/b1/sessions/s1/res-1/file.bin",
            1_048_576,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal("tenant-a", record.TenantId);
        Assert.Equal("bot-1", record.BotId);
        Assert.Equal("session-1", record.SessionId);
        Assert.Equal("res-1", record.ResourceId);
        Assert.Equal(1_048_576, record.SizeBytes);
    }

    private sealed class FakeCompensationRepo : IAssetCompensationRepository
    {
        public readonly List<(string TenantId, string ObjectKey, string State)> Recorded = [];

        public Task RecordTempObjectAsync(string tenantId, string objectKey, string state, CancellationToken ct)
        {
            Recorded.Add((tenantId, objectKey, state));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CompensationRecord>> ListPendingCompensationAsync(string tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CompensationRecord>>([]);

        public Task MarkResolvedAsync(long id, CancellationToken ct) => Task.CompletedTask;
    }
}
