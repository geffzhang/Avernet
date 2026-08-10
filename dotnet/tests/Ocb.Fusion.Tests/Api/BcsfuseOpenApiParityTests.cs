using System.Diagnostics.CodeAnalysis;
using Ocb.Fusion.OpenApi;

namespace Ocb.Fusion.Tests.Api;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BcsfuseOpenApiParityTests
{
    [Fact]
    public void OpenApiParity_BaselineMustContainFuseAndWorkersPaths()
    {
        var baseline = BcsfuseOpenApiParityValidator.ReadBaselinePaths(
            Path.Combine("..", "..", "..", "..", "..", "contracts", "parity-corpus", "bcsfuse.openapi.yaml"));

        var diff = BcsfuseOpenApiParityValidator.Compare(baseline);
        Assert.Empty(diff.MissingPaths);
    }

    [Fact]
    public void OpenApiParity_MustContainFusePath()
    {
        var baseline = BcsfuseOpenApiParityValidator.ReadBaselinePaths(
            Path.Combine("..", "..", "..", "..", "..", "contracts", "parity-corpus", "bcsfuse.openapi.yaml"));

        Assert.Contains(baseline, p => string.Equals(p, "/api/v1/groups/{group_id}/fuse", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenApiParity_MustContainWorkersPath()
    {
        var baseline = BcsfuseOpenApiParityValidator.ReadBaselinePaths(
            Path.Combine("..", "..", "..", "..", "..", "contracts", "parity-corpus", "bcsfuse.openapi.yaml"));

        Assert.Contains(baseline, p => string.Equals(p, "/v1/workers", StringComparison.Ordinal));
        Assert.Contains(baseline, p => string.Equals(p, "/v1/workers/{worker_id}", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenApiParity_MustContainHealthPath()
    {
        var baseline = BcsfuseOpenApiParityValidator.ReadBaselinePaths(
            Path.Combine("..", "..", "..", "..", "..", "contracts", "parity-corpus", "bcsfuse.openapi.yaml"));

        Assert.Contains(baseline, p => string.Equals(p, "/health", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiredPathSet_KnownPaths()
    {
        var required = BcsfuseOpenApiParityValidator.RequiredBcsfusePaths;
        Assert.Contains("/v1/workers", required);
        Assert.Contains("/v1/workers/{worker_id}", required);
        Assert.Contains("/api/v1/groups/{group_id}/fuse", required);
        Assert.Contains("/health", required);
        Assert.Equal(4, required.Count);
    }
}
