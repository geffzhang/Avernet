using Ocb.GrainContracts.Bot;
using Orleans;

namespace Ocb.Grains.Bot;

/// <summary>
/// Grain that owns the authoritative desired/observed skills state for a bot.
/// Key format: <c>bot/{tenantId}/{botId}</c>.
/// Enforces tenant isolation on every command.
/// </summary>
public sealed class BotGrain : Grain, IBotGrain
{
    private readonly IPersistentState<BotGrainState> _state;

    public BotGrain(
        [PersistentState("bot-skills", "orleans-storage")]
        IPersistentState<BotGrainState> state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public async Task ReconcileSkillsAsync(BotSkillReconciliationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTenant(command.TenantId);

        _state.State.DesiredSkillIds = [..command.SkillIds];
        _state.State.State = "reconciling";
        _state.State.LastReconciledAt = DateTimeOffset.UtcNow;

        // In a full deployment, the grain would call SkillActivationService here
        // to compute the activated-only plan and delegate to the Runtime Worker.
        // For now, the state tracks desired skills and the actual activation
        // is driven by a separate orchestration layer.

        await _state.WriteStateAsync();
    }

    /// <inheritdoc />
    public Task<BotSkillsSnapshot> GetSkillsSnapshotAsync()
    {
        return Task.FromResult(new BotSkillsSnapshot(
            DesiredSkillIds: [.._state.State.DesiredSkillIds],
            ActiveSkillIds: [.._state.State.ActiveSkillIds],
            State: _state.State.State,
            ActiveViewId: _state.State.ActiveViewId,
            LastReconciledAt: _state.State.LastReconciledAt));
    }

    /// <summary>
    /// Extracts the tenant from the grain key and validates it matches the command.
    /// </summary>
    private void ValidateTenant(string requestTenantId)
    {
        // Key format: bot/{tenantId}/{botId}
        var key = this.GetPrimaryKeyString();
        var parts = key.Split('/');
        if (parts.Length < 3
            || !string.Equals(parts[0], "bot", StringComparison.Ordinal)
            || !string.Equals(parts[1], requestTenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Tenant mismatch: grain is scoped to a different tenant than the request. " +
                $"Key='{key}', requestTenantId='{requestTenantId}'");
        }
    }
}
