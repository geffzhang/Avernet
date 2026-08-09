using Ocb.GrainContracts.Channels;

namespace Ocb.Grains.Channels;

/// <summary>
/// Persistent state for a tenant-scoped connection directory grain.
/// Each tenant has its own grain, so this dictionary only contains
/// connections belonging to a single tenant.
/// </summary>
[Alias("Ocb.Grains.Channels.ConnectionDirectoryState")]
[GenerateSerializer]
public sealed class ConnectionDirectoryState
{
    /// <summary>
    /// Active connections keyed by connection identifier.
    /// </summary>
    [Id(0)]
    public Dictionary<string, ConnectionRoute> ByConnectionId { get; init; } = new(StringComparer.Ordinal);
}
