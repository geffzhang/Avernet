using System.Diagnostics.CodeAnalysis;
using Ocb.Contracts.Skills;
using Ocb.Runtime.Worker.Application.Skills;

namespace Ocb.Runtime.Worker.Tests.Skills;

/// <summary>
/// Verify skills materialization boundary:
/// - Empty skills list is rejected
/// - Invalid skill version is rejected
/// - Valid skills are materialized
/// - Retry policy produces correct delays
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SkillsMaterializationBoundaryTests
{
    private readonly SkillsMaterializationCoordinator _service;

    public SkillsMaterializationBoundaryTests()
    {
        _service = new SkillsMaterializationCoordinator();
    }

    [Fact]
    public async Task Materialize_EmptySkills_ShouldReject()
    {
        var request = new MaterializationRequest(
            new CallerContext("t1", "u1"),
            "bot-1", "1.0",
            Array.Empty<ActivatedSkillVersion>());

        var result = await _service.MaterializeActivatedSkillsAsync(request, default);

        Assert.False(result.Succeeded);
        Assert.Equal("NO_ACTIVATED_SKILLS", result.ErrorCode);
        Assert.Equal("NO_SKILLS", result.ObservedState);
    }

    [Fact]
    public async Task Materialize_InvalidSkillVersion_ShouldReject()
    {
        var request = new MaterializationRequest(
            new CallerContext("t1", "u1"),
            "bot-1", "1.0",
            new[]
            {
                new ActivatedSkillVersion(
                    SkillId: "",
                    SourceLocator: "",
                    ImmutableVersion: "",
                    ExpectedSha256: "abc"),
            });

        var result = await _service.MaterializeActivatedSkillsAsync(request, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_SKILL_VERSION", result.ErrorCode);
    }

    [Fact]
    public async Task Materialize_ValidSkills_ShouldSucceed()
    {
        var request = new MaterializationRequest(
            new CallerContext("t1", "u1"),
            "bot-1", "1.0",
            new[]
            {
                new ActivatedSkillVersion(
                    SkillId: "my-skill",
                    SourceLocator: "https://registry/skills/my-skill@v1",
                    ImmutableVersion: "v1.0.0",
                    ExpectedSha256: "sha256:abcdef"),
            });

        var result = await _service.MaterializeActivatedSkillsAsync(request, default);

        Assert.True(result.Succeeded);
        Assert.Equal("MATERIALIZED", result.ObservedState);
        Assert.NotNull(result.ActiveViewId);
    }

    [Fact]
    public async Task Materialize_MultipleSkills_ShouldSucceed()
    {
        var request = new MaterializationRequest(
            new CallerContext("t1", "u1"),
            "bot-multi", "1.0",
            new[]
            {
                new ActivatedSkillVersion("s1", "loc1", "v1", "sha1"),
                new ActivatedSkillVersion("s2", "loc2", "v2", "sha2"),
                new ActivatedSkillVersion("s3", "loc3", "v3", "sha3"),
            });

        var result = await _service.MaterializeActivatedSkillsAsync(request, default);

        Assert.True(result.Succeeded);
    }
}
