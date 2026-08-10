using Orleans;

namespace Ocb.Grains.Bot;

/// <summary>
/// Persistent state for a BotGrain.
/// Tracks the desired and observed skills for a specific bot within a tenant.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.Grains.Bot.BotGrainState")]
public sealed class BotGrainState
{
    [Id(0)]
    public List<string> DesiredSkillIds { get; set; } = [];

    [Id(1)]
    public List<string> ActiveSkillIds { get; set; } = [];

    [Id(2)]
    public string State { get; set; } = "idle";

    [Id(3)]
    public string? ActiveViewId { get; set; }

    [Id(4)]
    public DateTimeOffset LastReconciledAt { get; set; } = DateTimeOffset.UtcNow;
}
