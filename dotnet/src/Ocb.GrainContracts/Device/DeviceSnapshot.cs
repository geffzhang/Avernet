namespace Ocb.GrainContracts.Device;

using Orleans;

/// <summary>
/// Snapshot of a device's current state.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Device.DeviceSnapshot")]
public sealed record DeviceSnapshot(
    [property: Id(0)] string DeviceId,
    [property: Id(1)] string TenantId,
    [property: Id(2)] string BotId,
    [property: Id(3)] string TemplateUuid,
    [property: Id(4)] string SandboxProfile,
    [property: Id(5)] string Status,
    [property: Id(6)] string? SandboxId,
    [property: Id(7)] string? InternalEndpoint,
    [property: Id(8)] DateTimeOffset CreatedAt,
    [property: Id(9)] DateTimeOffset? ExpiresAt,
    [property: Id(10)] DateTimeOffset UpdatedAt);
