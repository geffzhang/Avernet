using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Ocb.Infrastructure.Minio.Storage;
using Ocb.PluginApi.Storage;

namespace Ocb.Infrastructure.Tests.Minio;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class MultipartAndChecksumTests
{
    [Fact]
    public void MinioAssetStoragePlugin_ImplementsAssetObjectStoragePlugin()
    {
        Assert.True(typeof(MinioAssetObjectStoragePlugin).IsAssignableTo(typeof(IAssetObjectStoragePlugin)));
    }

    [Fact]
    public void BuildObjectKey_IsTenantScoped()
    {
        var keyA = MinioAssetObjectStoragePlugin.BuildObjectKey("tenant-a", "bot-1",
            "session-1", "res-1", "file.txt");

        var keyB = MinioAssetObjectStoragePlugin.BuildObjectKey("tenant-b", "bot-1",
            "session-1", "res-1", "file.txt");

        Assert.StartsWith("tenant/tenant-a/", keyA, StringComparison.Ordinal);
        Assert.StartsWith("tenant/tenant-b/", keyB, StringComparison.Ordinal);
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void BuildObjectKey_ContainsFullResourcePath()
    {
        var key = MinioAssetObjectStoragePlugin.BuildObjectKey("t1", "b1",
            "s1", "r1", "report.pdf");

        Assert.Equal("tenant/t1/bots/b1/sessions/s1/r1/report.pdf", key);
    }

    [Fact]
    public async Task ComputeSha256Async_IsDeterministic()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        using var streamA = new MemoryStream(content);
        using var streamB = new MemoryStream(content);

        var hashA = await ChecksumVerifier.ComputeSha256Async(streamA);
        var hashB = await ChecksumVerifier.ComputeSha256Async(streamB);

        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length); // SHA-256 hex is 64 chars
    }

    [Fact]
    public async Task VerifyString_Throws_OnMismatch()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ChecksumVerifier.VerifyString("aaa", "bbb"));

        Assert.Contains("Checksum mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyString_Succeeds_OnMatch_CaseInsensitive()
    {
        var ex = Record.Exception(() =>
            ChecksumVerifier.VerifyString("ABCDEF", "abcdef"));

        Assert.Null(ex);
    }
}
