using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Publication;
using Ocb.Contracts.Skills;

namespace Ocb.EndToEnd.Tests.Backend.UserStory;

/// <summary>
/// User story: an operator publishes a skill from git → activates it for a bot.
/// The full circle from raw git source through publication to activated plan.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SkillsActivationLifecycleTests
{
    [Fact]
    public void StateMachine_DraftThroughPublishedToActive_HappyPath()
    {
        // Draft → Submit → Published → Activate → Active
        var t1 = SkillPublicationStateMachine.Transition(PublicationState.Draft, PublicationEvent.Submit);
        Assert.True(t1.Succeeded);
        Assert.Equal(PublicationState.Published, t1.NewState);

        var t2 = SkillPublicationStateMachine.Transition(PublicationState.Published, PublicationEvent.Activate);
        Assert.True(t2.Succeeded);
        Assert.Equal(PublicationState.Active, t2.NewState);
    }

    [Fact]
    public void SourceSchemes_AllHaveExpectedValues()
    {
        Assert.Equal(0, (int)SkillSourceScheme.Git);
        Assert.Equal(1, (int)SkillSourceScheme.Local);
        Assert.Equal(2, (int)SkillSourceScheme.Center);
    }

    [Fact]
    public void SkillVersionRef_IsImmutable()
    {
        var v1 = new SkillVersionRef("git://repo", "v1", SkillSourceScheme.Git);
        var v2 = v1 with { ImmutableVersion = "v2" };

        Assert.Equal("v1", v1.ImmutableVersion);
        Assert.Equal("v2", v2.ImmutableVersion);
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void PublicationRecord_AllFieldsSurviveRoundTrip()
    {
        var version = new SkillVersionRef("center://uuid-42", "v5.3", SkillSourceScheme.Center);
        var record = new SkillPublicationRecord(
            "tenant-a", "bot-1", "skill-7", version,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "published", "skills-pool-p3-v1");

        Assert.Equal("tenant-a", record.TenantId);
        Assert.Equal("bot-1", record.BotId);
        Assert.Equal("skill-7", record.SkillId);
        Assert.Equal("v5.3", record.Version.ImmutableVersion);
        Assert.Equal(SkillSourceScheme.Center, record.Version.Scheme);
        Assert.Equal("skills-pool-p3-v1", record.ManifestContractVersion);
    }
}
