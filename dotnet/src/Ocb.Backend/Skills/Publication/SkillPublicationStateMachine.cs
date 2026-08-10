namespace Ocb.Backend.Skills.Publication;

/// <summary>
/// Publication states for a skill version lifecycle.
/// </summary>
public enum PublicationState
{
    Draft,
    Published,
    Active,
    Retired,
}

/// <summary>
/// Events that drive the publication state machine.
/// </summary>
public enum PublicationEvent
{
    Submit,
    ReviewPass,
    Activate,
    Retire,
}

/// <summary>
/// Result of a state transition attempt.
/// </summary>
public sealed record TransitionResult(bool Succeeded, PublicationState NewState, string? Error = null);

/// <summary>
/// Deterministic state machine for skill publication lifecycle transitions.
/// </summary>
public static class SkillPublicationStateMachine
{
    public static TransitionResult Transition(PublicationState current, PublicationEvent evt)
    {
        return (current, evt) switch
        {
            (PublicationState.Draft, PublicationEvent.Submit) => Success(PublicationState.Published),
            (PublicationState.Draft, PublicationEvent.Retire) => Success(PublicationState.Retired),

            (PublicationState.Published, PublicationEvent.Activate) => Success(PublicationState.Active),
            (PublicationState.Published, PublicationEvent.Retire) => Success(PublicationState.Retired),

            (PublicationState.Active, PublicationEvent.Retire) => Success(PublicationState.Retired),

            _ => new TransitionResult(false, current, $"Invalid transition: {current} -> {evt}"),
        };
    }

    private static TransitionResult Success(PublicationState newState) =>
        new(true, newState);
}
