namespace Ocb.GrainContracts.Device;

using Orleans;

/// <summary>
/// Grain that owns the lifecycle state for a single device.
/// Each grain is keyed <c>device/{deviceId}</c>.
/// </summary>
[Alias("Ocb.GrainContracts.Device.IDeviceGrain")]
public interface IDeviceGrain : IGrainWithStringKey
{
    /// <summary>
    /// Register or update a device's association with a tenant and bot.
    /// </summary>
    Task RegisterAsync(
        string tenantId, string botId, string templateUuid, string sandboxProfile);

    /// <summary>
    /// Update the device's observed status.
    /// </summary>
    Task SetStatusAsync(string status, DateTimeOffset? expiresAt);

    /// <summary>
    /// Return the current device state snapshot.
    /// </summary>
    Task<DeviceSnapshot> GetSnapshotAsync();
}
