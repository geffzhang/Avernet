using Ocb.Contracts.Identity;

namespace Ocb.PluginApi.Identity;

/// <summary>
/// Plugin port for the caller identity repository.
/// Implemented by the PostgreSQL infrastructure layer under the ocb_business schema.
/// </summary>
public interface ICallerIdentityRepositoryPlugin : IPluginContract
{
    /// <summary>
    /// Look up the caller identity binding for a specific subject within a tenant and bot.
    /// Returns null when no binding exists.
    /// </summary>
    Task<CallerIdentityBinding?> GetCallerIdentityAsync(
        string tenantId, string botId, string subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// List all caller identities for a given tenant and bot.
    /// </summary>
    Task<IReadOnlyList<CallerIdentityBinding>> ListByBotAsync(
        string tenantId, string botId, CancellationToken cancellationToken);
}
