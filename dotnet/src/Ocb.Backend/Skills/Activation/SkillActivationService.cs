using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;
using CallerCtx = Ocb.Contracts.CallerContext;

namespace Ocb.Backend.Skills.Activation;

/// <summary>
/// Orchestrates skill activation: builds the activated-only plan from published
/// records and delegates materialization to the Runtime Worker.
/// </summary>
public sealed class SkillActivationService
{
    private readonly ISkillPublicationStorePlugin _store;
    private readonly IRuntimeSkillMaterializationService _runtime;

    public SkillActivationService(
        ISkillPublicationStorePlugin store,
        IRuntimeSkillMaterializationService runtime)
    {
        _store = store;
        _runtime = runtime;
    }

    /// <summary>
    /// Activate skills for a bot: build the activated-only plan, send it to
    /// the Runtime Worker, and return the materialization result.
    /// </summary>
    public async Task<MaterializationResult> ActivateAsync(
        CallerCtx caller,
        string botId,
        IReadOnlyList<string> skillIds,
        string manifestContractVersion,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);
        ArgumentNullException.ThrowIfNull(skillIds);

        var records = await _store.ListByBotAsync(caller.TenantId, botId, ct);
        var plan = SkillActivationPlanner.BuildActivatedOnlyPlan(records);

        var request = new MaterializationRequest(
            new CallerContext(caller.TenantId, caller.SubjectId),
            botId,
            manifestContractVersion,
            plan);

        return await _runtime.MaterializeActivatedSkillsAsync(request, ct);
    }
}
