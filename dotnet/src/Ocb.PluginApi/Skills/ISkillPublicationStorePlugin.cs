using Ocb.Contracts.Skills;

namespace Ocb.PluginApi.Skills;

/// <summary>
/// Plugin port for the skill publication persistence store.
/// Implemented by the PostgreSQL infrastructure layer under the ocb_business schema.
/// Every operation is tenant-scoped; the implementation must always include
/// a tenant-id predicate.
/// </summary>
public interface ISkillPublicationStorePlugin : IPluginContract
{
    /// <summary>
    /// Look up a single published skill by tenant, bot, and skill id.
    /// Returns null when no matching row exists.
    /// </summary>
    Task<SkillPublicationRecord?> GetBySkillAsync(
        string tenantId, string botId, string skillId, CancellationToken cancellationToken);

    /// <summary>
    /// List all published skills for a given tenant and bot.
    /// </summary>
    Task<IReadOnlyList<SkillPublicationRecord>> ListByBotAsync(
        string tenantId, string botId, CancellationToken cancellationToken);

    /// <summary>
    /// Insert a new publication record. Must reject duplicates via unique constraint.
    /// </summary>
    Task InsertAsync(
        SkillPublicationRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Update only the publication state of an existing record.
    /// </summary>
    Task UpdatePublicationStateAsync(
        string tenantId, string botId, string skillId, string publicationState,
        CancellationToken cancellationToken);
}
