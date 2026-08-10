using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Sessions;
using Ocb.Contracts;
using Ocb.Contracts.Resources;
using Ocb.PluginApi.Storage;

namespace Ocb.Backend.Tests.Sessions;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SessionAssetServiceTests
{
    private readonly FakeAssetStorage _storage = new();
    private readonly SessionAssetService _service;

    public SessionAssetServiceTests()
    {
        _service = new SessionAssetService(_storage);
    }

    [Fact]
    public async Task CreateUploadIntentAsync_ReturnsRecord_WithCorrectTenant()
    {
        var caller = new CallerContext("tenant-a", "u-1", new HashSet<string>(StringComparer.Ordinal) { "user" });

        var record = await _service.CreateUploadIntentAsync(
            caller, "bot-1", "session-1", "file.txt", 1024,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CancellationToken.None);

        Assert.Equal("tenant-a", record.TenantId);
        Assert.Equal("bot-1", record.BotId);
        Assert.Equal("session-1", record.SessionId);
        Assert.Equal(1024, record.SizeBytes);
        Assert.NotNull(record.ObjectKey);
        Assert.NotNull(record.ResourceId);
    }

    [Fact]
    public async Task CreateUploadIntentAsync_GeneratesUniqueObjectKeys()
    {
        var caller = new CallerContext("t1", "u1", new HashSet<string>(StringComparer.Ordinal));

        var r1 = await _service.CreateUploadIntentAsync(
            caller, "b1", "s1", "a.txt", 100, "abc123", CancellationToken.None);
        var r2 = await _service.CreateUploadIntentAsync(
            caller, "b1", "s1", "b.txt", 200, "def456", CancellationToken.None);

        Assert.NotEqual(r1.ObjectKey, r2.ObjectKey);
        Assert.NotEqual(r1.ResourceId, r2.ResourceId);
    }

    [Fact]
    public async Task CreateUploadIntentAsync_Throws_OnNullCaller()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.CreateUploadIntentAsync(
                null!, "b1", "s1", "f.txt", 100, "sha256", CancellationToken.None));
    }

    [Fact]
    public async Task CreateUploadIntentAsync_Throws_OnEmptyBotId()
    {
        var caller = new CallerContext("t1", "u1", new HashSet<string>(StringComparer.Ordinal));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateUploadIntentAsync(
                caller, "", "s1", "f.txt", 100, "sha256", CancellationToken.None));
    }

    [Fact]
    public async Task CreateUploadIntentAsync_Throws_OnEmptySessionId()
    {
        var caller = new CallerContext("t1", "u1", new HashSet<string>(StringComparer.Ordinal));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateUploadIntentAsync(
                caller, "b1", "", "f.txt", 100, "sha256", CancellationToken.None));
    }

    private sealed class FakeAssetStorage : IAssetObjectStoragePlugin
    {
        public Task<MultipartInitResult> BeginMultipartUploadAsync(
            AssetUploadRequest request, CancellationToken ct)
        {
            var objectKey = $"tenant/{request.TenantId}/bots/{request.BotId}/{request.ResourceId}";
            return Task.FromResult(new MultipartInitResult(
                Guid.NewGuid().ToString("N"), objectKey, []));
        }

        public Task<DownloadResult> DownloadWithIntegrityAsync(
            AssetDownloadRequest request, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<Uri> CreateSignedDownloadUrlAsync(
            string tenantId, string objectKey, TimeSpan lifetime, CancellationToken ct) =>
            throw new NotImplementedException();
    }
}
