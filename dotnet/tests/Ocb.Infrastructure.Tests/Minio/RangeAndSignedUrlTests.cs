using System.Diagnostics.CodeAnalysis;
using Ocb.Infrastructure.Minio.Storage;

namespace Ocb.Infrastructure.Tests.Minio;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class RangeAndSignedUrlTests
{
    [Fact]
    public void MinioOptions_HasRequiredFields()
    {
        var options = new MinioOptions(
            Endpoint: "play.min.io",
            AccessKey: "AK",
            SecretKey: "SK",
            BucketName: "avernet",
            UseSsl: true);

        Assert.Equal("play.min.io", options.Endpoint);
        Assert.Equal("avernet", options.BucketName);
        Assert.True(options.UseSsl);
    }

    [Fact]
    public void AssetUploadRequest_CarriesAllMetadata()
    {
        var request = new Ocb.PluginApi.Storage.AssetUploadRequest(
            "tenant-a", "bot-1", "session-1", "res-1",
            "large-file.bin", 1_048_576,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal("tenant-a", request.TenantId);
        Assert.Equal("bot-1", request.BotId);
        Assert.Equal(1_048_576, request.SizeBytes);
    }

    [Fact]
    public void AssetDownloadRequest_SupportsOptionalRange()
    {
        var withoutRange = new Ocb.PluginApi.Storage.AssetDownloadRequest(
            "tenant-a", "tenant/t1/b1/key",
            "sha256");

        Assert.Null(withoutRange.RangeStart);
        Assert.Null(withoutRange.RangeLength);

        var withRange = new Ocb.PluginApi.Storage.AssetDownloadRequest(
            "tenant-a", "tenant/t1/b1/key",
            "sha256", 0, 1_048_576);

        Assert.Equal(0, withRange.RangeStart);
        Assert.Equal(1_048_576, withRange.RangeLength);
    }
}
