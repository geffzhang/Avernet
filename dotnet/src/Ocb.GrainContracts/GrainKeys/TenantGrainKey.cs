using Ocb.Contracts;

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

    /// <summary>
    /// Returns the grain key for a tenant's fusion job.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="fusionJobId">The fusion job identifier.</param>
    /// <returns>A grain key in the form <c>fusion-job/{tenantId}/{fusionJobId}</c>.</returns>
    public static string FusionJob(string tenantId, string fusionJobId) => $"fusion-job/{tenantId}/{fusionJobId}";

    /// <summary>
    /// Returns the grain key for a bot within a tenant.
    /// </summary>
    public static string Bot(string tenantId, string botId) => $"bot/{tenantId}/{botId}";

    /// <summary>
    /// Returns the grain key for a session within a tenant.
    /// </summary>
    public static string Session(string tenantId, string sessionId) => $"session/{tenantId}/{sessionId}";

    /// <summary>
    /// Returns the grain key for a device.
    /// </summary>
    public static string Device(string deviceId) => $"device/{deviceId}";

    /// <summary>
    /// Builds a deterministic, tenant-scoped grain key from a tenant id
    /// and entity id. The result is stable across processes and is safe as
    /// a grain identity prefix or tag.
    /// </summary>
    public static string Build(string tenantId, string entityId)
        => TenantEntityKey.Create(tenantId, entityId).ToString();
}
