using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Assets;

namespace Ocb.Backend.Tests.Assets;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class AssetCompensationServiceTests
{
    private sealed class FakeCompensationRepo : IAssetCompensationRepository
    {
        public readonly List<(string TenantId, string ObjectKey, string State)> Recorded = [];

        public Task RecordTempObjectAsync(string tenantId, string objectKey, string state, CancellationToken ct)
        {
            Recorded.Add((tenantId, objectKey, state));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CompensationRecord>> ListPendingCompensationAsync(string tenantId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<CompensationRecord>>([]);
        }

        public Task MarkResolvedAsync(long id, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RecordTempObjectOnFailure_WithError_RecordsTEMP_UPLOADED()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);
        var error = new InvalidOperationException("MinIO upload failed");

        await service.RecordTempObjectOnFailureAsync("t1", "tenant/t1/file.bin", error, CancellationToken.None);

        var record = Assert.Single(fake.Recorded);
        Assert.Equal("TEMP_UPLOADED", record.State);
    }

    [Fact]
    public async Task RecordTempObjectOnFailure_WithoutError_RecordsPENDING_CLEANUP()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);

        await service.RecordTempObjectOnFailureAsync("t1", "tenant/t1/file.bin", null, CancellationToken.None);

        var record = Assert.Single(fake.Recorded);
        Assert.Equal("PENDING_CLEANUP", record.State);
    }

    [Fact]
    public async Task RecordTempObjectOnFailure_PreservesTenantIdAndObjectKey()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);

        await service.RecordTempObjectOnFailureAsync("tenant-x", "tenant/tx/bot/key", null, CancellationToken.None);

        var record = Assert.Single(fake.Recorded);
        Assert.Equal("tenant-x", record.TenantId);
        Assert.Equal("tenant/tx/bot/key", record.ObjectKey);
    }

    [Fact]
    public async Task ExecuteWithCompensation_OnSuccess_DoesNotRecord()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);
        var operationCalled = false;

        await service.ExecuteWithCompensationAsync(
            "t1", "tenant/t1/file.bin",
            _ => { operationCalled = true; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(operationCalled);
        Assert.Empty(fake.Recorded);
    }

    [Fact]
    public async Task ExecuteWithCompensation_OnFailure_RecordsAndRethrows()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteWithCompensationAsync(
                "t1", "tenant/t1/file.bin",
                _ => throw new InvalidOperationException("DB failure"),
                CancellationToken.None));

        Assert.Contains("DB failure", ex.Message, StringComparison.Ordinal);

        var record = Assert.Single(fake.Recorded);
        Assert.Equal("t1", record.TenantId);
        Assert.Equal("tenant/t1/file.bin", record.ObjectKey);
        Assert.Equal("TEMP_UPLOADED", record.State);
    }

    [Fact]
    public async Task ExecuteWithCompensation_RejectsNullTenantId()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExecuteWithCompensationAsync("", "k", _ => Task.CompletedTask, CancellationToken.None));

        Assert.Contains("tenantId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteWithCompensation_RejectsNullOperation()
    {
        var fake = new FakeCompensationRepo();
        var service = new AssetCompensationService(fake);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ExecuteWithCompensationAsync("t1", "k", null!, CancellationToken.None));
    }
}
