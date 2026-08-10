using System.Diagnostics.CodeAnalysis;
using Ocb.Infrastructure.Vector.Guards;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Tests.Vector;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class VectorCompatibilityGuardTests
{
    private static readonly CollectionMeta MetaM1 = new(Model: "m1", Dimension: 1024);

    [Fact]
    public void ValidateOrThrow_WhenModelMismatch_ShouldThrow()
    {
        var ex = Assert.Throws<VectorCompatibilityException>(
            () => VectorCompatibilityGuard.ValidateOrThrow("m2", 1024, MetaM1));
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOrThrow_WhenDimensionMismatch_ShouldThrow()
    {
        var ex = Assert.Throws<VectorCompatibilityException>(
            () => VectorCompatibilityGuard.ValidateOrThrow("m1", 768, MetaM1));
        Assert.Contains("dimension", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOrThrow_WhenBothMismatch_ShouldThrowOnModelFirst()
    {
        var ex = Assert.Throws<VectorCompatibilityException>(
            () => VectorCompatibilityGuard.ValidateOrThrow("m2", 768, MetaM1));
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOrThrow_WhenMatching_ShouldNotThrow()
    {
        var ex = Record.Exception(
            () => VectorCompatibilityGuard.ValidateOrThrow("m1", 1024, MetaM1));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateOrThrow_ShouldThrowOnNullModel()
    {
        Assert.Throws<ArgumentNullException>(
            () => VectorCompatibilityGuard.ValidateOrThrow(null!, 1024, MetaM1));
    }

    [Fact]
    public void ValidateOrThrow_ShouldThrowOnNullMeta()
    {
        Assert.Throws<ArgumentNullException>(
            () => VectorCompatibilityGuard.ValidateOrThrow("m1", 1024, null!));
    }
}
