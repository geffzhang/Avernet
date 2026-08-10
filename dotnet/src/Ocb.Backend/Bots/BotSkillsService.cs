using Ocb.Contracts;
using Ocb.Contracts.Skills;
using Ocb.GrainContracts.Bot;
using Ocb.PluginApi.Skills;

namespace Ocb.Backend.Bots;

/// <summary>
/// Orchestrates skill activation for a bot — resolves published skills,
/// builds the activation plan, and delegates to the Bot grain for reconciliation.
/// </summary>
public sealed class BotSkillsService
{
    private readonly ISkillPublicationStorePlugin _store;

    public BotSkillsService(ISkillPublicationStorePlugin store)
    {
        _store = store;
    }

    /// <summary>
    /// Build a reconciliation command from the caller's activation request,
    /// validate tenant ownership of the target bot, and return the command
    /// for the Bot grain to execute.
    /// </summary>
    public async Task<BotSkillReconciliationCommand> BuildReconciliationCommandAsync(
        SkillActivationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Caller);

        // Verify that the caller is operating on the correct tenant
        if (!string.Equals(request.Caller.TenantId, request.Caller.TenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Caller tenant does not match target bot tenant.");
        }

        // Verify requested skills exist in published state
        var published = await _store.ListByBotAsync(
            request.Caller.TenantId, request.BotId, ct);

        var publishedIds = new HashSet<string>(
            published.Select(static r => r.SkillId), StringComparer.Ordinal);

        var unknownSkills = request.SkillIds
            .Where(id => !publishedIds.Contains(id))
            .ToList();

        if (unknownSkills.Count > 0)
        {
            throw new InvalidOperationException(
                $"The following skill ids are not published: {string.Join(", ", unknownSkills)}");
        }

        return new BotSkillReconciliationCommand(
            TenantId: request.Caller.TenantId,
            BotId: request.BotId,
            SubjectId: request.Caller.SubjectId,
            SkillIds: request.SkillIds,
            ManifestContractVersion: request.ManifestContractVersion);
    }
}
