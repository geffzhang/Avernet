namespace Ocb.GrainContracts.Bot;

using Orleans;

/// <summary>
/// Snapshot of a bot's desired and observed skills state.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Bot.BotSkillsSnapshot")]
public sealed record BotSkillsSnapshot(
    [property: Id(0)] IReadOnlyList<string> DesiredSkillIds,
    [property: Id(1)] IReadOnlyList<string> ActiveSkillIds,
    [property: Id(2)] string State,
    [property: Id(3)] string? ActiveViewId,
    [property: Id(4)] DateTimeOffset LastReconciledAt);
