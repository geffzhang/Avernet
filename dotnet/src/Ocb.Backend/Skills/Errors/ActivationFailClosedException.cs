namespace Ocb.Backend.Skills.Errors;

/// <summary>
/// Thrown when activation fails without a previous active view to roll back to.
/// This is a fail-closed condition: no safe fallback exists.
/// </summary>
public sealed class ActivationFailClosedException : InvalidOperationException
{
    public ActivationFailClosedException(string message) : base(message)
    {
    }

    public ActivationFailClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
