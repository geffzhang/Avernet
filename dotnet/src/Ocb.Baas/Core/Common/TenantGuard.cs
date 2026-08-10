using Ocb.Contracts;

namespace Ocb.Baas.Core.Common;

/// <summary>
/// Enforces tenant-scoped access — verifies that the caller's tenant
/// matches the resource's tenant, throwing a domain error on mismatch.
/// </summary>
public static class TenantGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the tenant IDs
    /// don't match (access denied to another tenant's resources).
    /// </summary>
    public static void AssertTenantMatch(CallerContext caller, string resourceTenantId)
    {
        if (!string.Equals(caller.TenantId, resourceTenantId, StringComparison.Ordinal))
        {
            throw new TenantMismatchException(
                $"Caller tenant '{caller.TenantId}' cannot access resources of tenant '{resourceTenantId}'.");
        }
    }
}

/// <summary>
/// Thrown when a caller attempts to access another tenant's resources.
/// </summary>
public sealed class TenantMismatchException : InvalidOperationException
{
    public TenantMismatchException(string message) : base(message) { }
}
