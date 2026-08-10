using Ocb.GrainContracts.Session;
using Orleans;

namespace Ocb.Grains.Session;

/// <summary>
/// Persistent state for a SessionGrain.
/// Tracks uploaded assets and session lifecycle.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.Grains.Session.SessionGrainState")]
public sealed class SessionGrainState
{
    [Id(0)]
    public List<SessionAssetEntry> Entries { get; set; } = [];

    [Id(1)]
    public bool IsClosed { get; set; }
}
