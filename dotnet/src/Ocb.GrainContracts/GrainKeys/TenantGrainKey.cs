namespace Ocb.GrainContracts.GrainKeys;

/// <summary>
/// Provides factory methods for tenant-scoped Orleans grain keys.
/// </summary>
public static class TenantGrainKey
{
    /// <summary>
    /// Returns the grain key for a tenant's connection directory.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A grain key in the form <c>directory/{tenantId}</c>.</returns>
    public static string Directory(string tenantId) => $"directory/{tenantId}";
}
