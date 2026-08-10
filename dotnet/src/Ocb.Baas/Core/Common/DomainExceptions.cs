namespace Ocb.Baas.Core.Common;

/// <summary>
/// Thrown when a requested resource is not found.
/// </summary>
public sealed class ResourceNotFoundException : InvalidOperationException
{
    public string ResourceCode { get; }

    public ResourceNotFoundException(string resourceCode, string? message = null)
        : base(message ?? $"Resource not found: {resourceCode}")
    {
        ResourceCode = resourceCode;
    }
}

/// <summary>
/// Thrown when a domain state transition is invalid.
/// </summary>
public sealed class DomainConflictException : InvalidOperationException
{
    public string ConflictCode { get; }

    public DomainConflictException(string conflictCode, string? message = null)
        : base(message ?? $"Domain conflict: {conflictCode}")
    {
        ConflictCode = conflictCode;
    }
}
