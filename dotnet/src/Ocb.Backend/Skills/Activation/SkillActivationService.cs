using Ocb.Backend.Skills.Errors;
using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;
using CallerCtx = Ocb.Contracts.CallerContext;

namespace Ocb.Backend.Skills.Activation;

/// <summary>
/// Orchestrates skill activation: builds the activated-only plan, delegates
/// materialization to the Runtime Worker exactly once (no backend retry),
/// records observed state, and enforces fail-closed activation.
/// </summary>
public sealed class SkillActivationService
{
    private readonly ISkillPublicationStorePlugin _store;
    private readonly IRuntimeSkillMaterializationService _runtime;
    private readonly IObservedStateStore _observedStore;

    public SkillActivationService(
        ISkillPublicationStorePlugin store,
        IRuntimeSkillMaterializationService runtime,
        IObservedStateStore observedStore)
    {
        _store = store;
        _runtime = runtime;
        _observedStore = observedStore;
    }

    /// <summary>
    /// Activate skills for a bot exactly once — no backend-side retry.
    /// Records integrity failures and rollback outcomes. Throws
    /// <see cref="ActivationFailClosedException"/> when activation fails
    /// without a previous active view.
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

        // Call runtime exactly once — no retry loop in backend.
        var result = await _runtime.MaterializeActivatedSkillsAsync(request, ct);

        // Fail-closed: no previous view means no safe fallback.
        if (!result.Succeeded && result.ErrorCode == "ACTIVATION_NO_PREVIOUS_VIEW")
        {
            throw new ActivationFailClosedException(
                "Activation failed with no previous view — fail-closed. " +
                "A previously active view is required for safe rollback.");
        }

        // Record observed state whether success or failure (rollback/retry belongs to Runtime).
        await _observedStore.RecordAsync(
            caller.TenantId, botId,
            result.ObservedState, result.ActiveViewId, result.ErrorCode, ct);

        return result;
    }
}
