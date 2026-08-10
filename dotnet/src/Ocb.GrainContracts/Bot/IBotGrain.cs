namespace Ocb.GrainContracts.Bot;

using Orleans;

/// <summary>
/// Grain that owns the authoritative skills-desired/observed state for a bot.
/// Each grain is keyed <c>bot/{tenantId}/{botId}</c>.
/// </summary>
[Alias("Ocb.GrainContracts.Bot.IBotGrain")]
public interface IBotGrain : IGrainWithStringKey
{
    /// <summary>
    /// Reconcile the bot's active skills to match the desired activation plan.
    /// The grain validates tenant isolation, then delegates to the activation
    /// service for plan computation and Runtime Worker materialization.
    /// </summary>
    Task ReconcileSkillsAsync(BotSkillReconciliationCommand command);

    /// <summary>
    /// Return the current desired/observed snapshot of the bot's skills state.
    /// </summary>
    Task<BotSkillsSnapshot> GetSkillsSnapshotAsync();
}

/// <summary>
/// Command DTO for reconciling a bot's active skills.
/// Uses Orleans serialization; mapped to <c>SkillActivationRequest</c> at the grain boundary.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Bot.BotSkillReconciliationCommand")]
public sealed record BotSkillReconciliationCommand(
    [property: Id(0)] string TenantId,
    [property: Id(1)] string BotId,
    [property: Id(2)] string SubjectId,
    [property: Id(3)] IReadOnlyList<string> SkillIds,
    [property: Id(4)] string ManifestContractVersion);
