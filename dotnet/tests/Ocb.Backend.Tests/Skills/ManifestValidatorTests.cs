using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Errors;
using Ocb.Backend.Skills.Manifest;

namespace Ocb.Backend.Tests.Skills;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class ManifestValidatorTests
{
    [Fact]
    public void PoolManifest_RejectsUnknownContractVersion()
    {
        var manifest = new SkillsLayoutManifest("openclaw", "pool", "skills-pool-p4-v2");

        var ex = Assert.Throws<UnknownManifestContractException>(
            () => SkillsLayoutManifestValidator.ValidateOrThrow(manifest));

        Assert.Contains("skills-pool-p3-v1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("skills-pool-p4-v2", ex.Message, StringComparison.Ordinal);
        Assert.Equal("skills-pool-p4-v2", ex.DeclaredVersion);
    }

    [Fact]
    public void PoolManifest_AcceptsSupportedContractVersion()
    {
        var manifest = new SkillsLayoutManifest("openclaw", "pool", "skills-pool-p3-v1");

        var ex = Record.Exception(
            () => SkillsLayoutManifestValidator.ValidateOrThrow(manifest));

        Assert.Null(ex);
    }

    [Fact]
    public void NonPoolLayout_AcceptsAnyVersion()
    {
        // Non-pool layouts are not gated on contract version
        var manifest = new SkillsLayoutManifest("custom-layout", "standalone", "any-version-v99");

        var ex = Record.Exception(
            () => SkillsLayoutManifestValidator.ValidateOrThrow(manifest));

        Assert.Null(ex);
    }

    [Fact]
    public void PoolManifest_RejectsEmptyContractVersion()
    {
        var manifest = new SkillsLayoutManifest("openclaw", "pool", "");

        var ex = Assert.Throws<UnknownManifestContractException>(
            () => SkillsLayoutManifestValidator.ValidateOrThrow(manifest));

        Assert.Equal("", ex.DeclaredVersion);
    }

    [Fact]
    public void NullManifest_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => SkillsLayoutManifestValidator.ValidateOrThrow(null!));
    }

    [Fact]
    public void UnknownManifestContractException_IsAssignableTo_InvalidOperationException()
    {
        var exception = new UnknownManifestContractException("v-unknown");

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }
}
