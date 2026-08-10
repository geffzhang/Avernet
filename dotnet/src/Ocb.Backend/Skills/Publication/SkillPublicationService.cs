using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;

namespace Ocb.Backend.Skills.Publication;

/// <summary>
/// Service for publishing immutable skill versions. Enforces the rule that
/// <c>center://</c> source locators require an already-published record —
/// they are references to externally published content, not fresh publications.
/// </summary>
public sealed class SkillPublicationService
{
    private readonly ISkillPublicationStorePlugin _store;

    public SkillPublicationService(ISkillPublicationStorePlugin store)
    {
        _store = store;
    }

    /// <summary>
    /// Publish an immutable skill version. For <see cref="SkillSourceScheme.Center"/> sources,
    /// a prior published record must exist — center:// is a reference, not equivalent to
    /// the published state.
    /// </summary>
    public async Task<SkillPublicationRecord> PublishImmutableVersionAsync(
        string tenantId,
        string botId,
        string skillId,
        SkillVersionRef version,
        string packageSha256,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentNullException.ThrowIfNull(version);

        var entityState = "draft";

        if (version.Scheme == SkillSourceScheme.Center)
        {
            // center:// indicates a reference to externally-published content.
            // It is NOT equivalent to published — the caller must have gone
            // through the full publish workflow first.
            var prior = await _store.GetBySkillAsync(tenantId, botId, skillId, ct);
            if (prior is null
                || !string.Equals(prior.PublicationState, "published", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "center:// source is not equivalent to published state. " +
                    "The skill must have a prior published record before it can reference a center source.");
            }

            entityState = prior.PublicationState; // carried forward from prior published record
        }

        var record = new SkillPublicationRecord(
            TenantId: tenantId,
            BotId: botId,
            SkillId: skillId,
            Version: version,
            PackageSha256: packageSha256,
            PublicationState: entityState,
            ManifestContractVersion: "skills-pool-p3-v1");

        await _store.InsertAsync(record, ct);
        return record;
    }
}
