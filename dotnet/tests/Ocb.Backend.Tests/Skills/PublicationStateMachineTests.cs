using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Publication;

namespace Ocb.Backend.Tests.Skills;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PublicationStateMachineTests
{
    [Fact]
    public void Draft_Submit_TransitionsToPublished()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Draft, PublicationEvent.Submit);

        Assert.True(result.Succeeded);
        Assert.Equal(PublicationState.Published, result.NewState);
    }

    [Fact]
    public void Published_Activate_TransitionsToActive()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Published, PublicationEvent.Activate);

        Assert.True(result.Succeeded);
        Assert.Equal(PublicationState.Active, result.NewState);
    }

    [Fact]
    public void Active_Retire_TransitionsToRetired()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Active, PublicationEvent.Retire);

        Assert.True(result.Succeeded);
        Assert.Equal(PublicationState.Retired, result.NewState);
    }

    [Fact]
    public void Published_Retire_TransitionsToRetired()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Published, PublicationEvent.Retire);

        Assert.True(result.Succeeded);
        Assert.Equal(PublicationState.Retired, result.NewState);
    }

    [Fact]
    public void Draft_Activate_IsInvalid()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Draft, PublicationEvent.Activate);

        Assert.False(result.Succeeded);
        Assert.Equal(PublicationState.Draft, result.NewState);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Active_Submit_IsInvalid()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Active, PublicationEvent.Submit);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Retired_Any_IsInvalid()
    {
        foreach (var evt in Enum.GetValues<PublicationEvent>())
        {
            var result = SkillPublicationStateMachine.Transition(PublicationState.Retired, evt);
            Assert.False(result.Succeeded);
        }
    }

    [Fact]
    public void Draft_Retire_IsValid()
    {
        var result = SkillPublicationStateMachine.Transition(PublicationState.Draft, PublicationEvent.Retire);

        Assert.True(result.Succeeded);
        Assert.Equal(PublicationState.Retired, result.NewState);
    }
}
