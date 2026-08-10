using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Activation;
using Ocb.Contracts.Skills;

namespace Ocb.Backend.Tests.Skills;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class ActivationPlannerTests
{
    private static SkillPublicationRecord ActiveSkill(string skillId, string sourceLocator, string version = "v1")
        => new(
            TenantId: "t1",
            BotId: "b1",
            SkillId: skillId,
            Version: new SkillVersionRef(sourceLocator, version, SkillSourceScheme.Git),
            PackageSha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PublicationState: "active",
            ManifestContractVersion: "skills-pool-p3-v1");

    private static SkillPublicationRecord DraftSkill(string skillId)
        => new(
            TenantId: "t1",
            BotId: "b1",
            SkillId: skillId,
            Version: new SkillVersionRef("git://repo/skills/" + skillId, "v1", SkillSourceScheme.Git),
            PackageSha256: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            PublicationState: "draft",
            ManifestContractVersion: "skills-pool-p3-v1");

    private static SkillPublicationRecord PublishedSkill(string skillId)
        => new(
            TenantId: "t1",
            BotId: "b1",
            SkillId: skillId,
            Version: new SkillVersionRef("git://repo/skills/" + skillId, "v1", SkillSourceScheme.Git),
            PackageSha256: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            PublicationState: "published",
            ManifestContractVersion: "skills-pool-p3-v1");

    [Fact]
    public void ActivationPlan_DoesNotContainFullRepoBridgeEntries()
    {
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan([
            ActiveSkill("s1", "git://repo/skills-repo/"),
            ActiveSkill("s2", "git://repo/skills-repo/bridge/s1"),
            ActiveSkill("s3", "git://repo/skills/s3"),
        ]);

        Assert.DoesNotContain(plan, p =>
            p.SourceLocator.Contains("skills-repo", StringComparison.OrdinalIgnoreCase)
            && p.SourceLocator.EndsWith('/'));
        Assert.All(plan, p => Assert.False(
            p.SourceLocator.Contains("bridge", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void OnlyActivePublications_AreIncluded()
    {
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan([
            ActiveSkill("s1", "git://repo/s1"),
            DraftSkill("s2"),
            PublishedSkill("s3"),
        ]);

        Assert.Single(plan);
        Assert.Equal("s1", plan[0].SkillId);
    }

    [Fact]
    public void ActiveRecord_MapsToActivatedSkillVersion()
    {
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan([
            ActiveSkill("s1", "center://uuid-1", "v3.0"),
        ]);

        var entry = Assert.Single(plan);
        Assert.Equal("s1", entry.SkillId);
        Assert.Equal("center://uuid-1", entry.SourceLocator);
        Assert.Equal("v3.0", entry.ImmutableVersion);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", entry.ExpectedSha256);
    }

    [Fact]
    public void BridgePrefixedSource_IsExcluded()
    {
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan([
            ActiveSkill("s1", "local://bridge-to-repo/s1"),
        ]);

        Assert.Empty(plan);
    }

    [Fact]
    public void NullRecords_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SkillActivationPlanner.BuildActivatedOnlyPlan(null!));
    }

    [Fact]
    public void EmptyRecords_ReturnsEmpty()
    {
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan([]);

        Assert.Empty(plan);
    }

    [Fact]
    public void MultipleActive_AllIncluded()
    {
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan([
            ActiveSkill("s1", "git://repo/s1"),
            ActiveSkill("s2", "git://repo/s2"),
            ActiveSkill("s3", "center://c1/s3"),
        ]);

        Assert.Equal(3, plan.Count);
    }
}
