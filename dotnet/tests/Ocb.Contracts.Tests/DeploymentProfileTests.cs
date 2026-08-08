using Ocb.Configuration;

namespace Ocb.Contracts.Tests;

public sealed class DeploymentProfileTests
{
    [Theory]
    [InlineData(DeploymentProfile.Singlebox, VectorProvider.SonnetDb)]
    [InlineData(DeploymentProfile.Cluster, VectorProvider.Qdrant)]
    [InlineData(DeploymentProfile.Test, VectorProvider.InMemory)]
    public void ValidProfileProviderPairsPass(DeploymentProfile profile, VectorProvider provider)
    {
        var result = OcbPlatformOptionsValidator.Validate(new OcbPlatformOptions(profile, provider));
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(DeploymentProfile.Singlebox, VectorProvider.Qdrant)]
    [InlineData(DeploymentProfile.Singlebox, VectorProvider.InMemory)]
    [InlineData(DeploymentProfile.Cluster, VectorProvider.SonnetDb)]
    [InlineData(DeploymentProfile.Cluster, VectorProvider.InMemory)]
    [InlineData(DeploymentProfile.Test, VectorProvider.SonnetDb)]
    [InlineData(DeploymentProfile.Test, VectorProvider.Qdrant)]
    public void InvalidProfileProviderPairsFailClosed(DeploymentProfile profile, VectorProvider provider)
    {
        var result = OcbPlatformOptionsValidator.Validate(new OcbPlatformOptions(profile, provider));
        Assert.False(result.IsValid);
        Assert.Equal("vector_provider_not_allowed_for_profile", result.ErrorCode);
    }

    [Fact]
    public void UnknownProfileWithValidProviderFailsClosed()
    {
        var unknownProfile = (DeploymentProfile)12345;

        var result = OcbPlatformOptionsValidator.Validate(
            new OcbPlatformOptions(unknownProfile, VectorProvider.SonnetDb));

        Assert.False(result.IsValid);
        Assert.Equal("vector_provider_not_allowed_for_profile", result.ErrorCode);
    }

    [Fact]
    public void ValidProfileWithUnknownProviderFailsClosed()
    {
        var unknownProvider = (VectorProvider)12345;

        var result = OcbPlatformOptionsValidator.Validate(
            new OcbPlatformOptions(DeploymentProfile.Singlebox, unknownProvider));

        Assert.False(result.IsValid);
        Assert.Equal("vector_provider_not_allowed_for_profile", result.ErrorCode);
    }

    [Fact]
    public void ValidateNullOptionsThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OcbPlatformOptionsValidator.Validate(null!));
        Assert.Equal("options", exception.ParamName);
    }
}
