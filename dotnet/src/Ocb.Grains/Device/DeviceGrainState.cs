using Orleans;

namespace Ocb.Grains.Device;

/// <summary>
/// Persistent state for a DeviceGrain.
/// Tracks device registration and lifecycle status.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.Grains.Device.DeviceGrainState")]
public sealed class DeviceGrainState
{
    [Id(0)]
    public string TenantId { get; set; } = string.Empty;

    [Id(1)]
    public string BotId { get; set; } = string.Empty;

    [Id(2)]
    public string TemplateUuid { get; set; } = string.Empty;

    [Id(3)]
    public string SandboxProfile { get; set; } = string.Empty;

    [Id(4)]
    public string Status { get; set; } = "unregistered";

    [Id(5)]
    public string? SandboxId { get; set; }

    [Id(6)]
    public string? InternalEndpoint { get; set; }

    [Id(7)]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Id(8)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [Id(9)]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
