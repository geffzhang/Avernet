using Ocb.GrainContracts.Device;
using Orleans;

namespace Ocb.Grains.Device;

/// <summary>
/// Grain that owns the lifecycle state for a single device.
/// Key format: <c>device/{deviceId}</c>.
/// </summary>
public sealed class DeviceGrain : Grain, IDeviceGrain
{
    private readonly IPersistentState<DeviceGrainState> _state;

    public DeviceGrain(
        [PersistentState("device-lifecycle", "orleans-storage")]
        IPersistentState<DeviceGrainState> state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(
        string tenantId, string botId, string templateUuid, string sandboxProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);

        _state.State.TenantId = tenantId;
        _state.State.BotId = botId;
        _state.State.TemplateUuid = templateUuid;
        _state.State.SandboxProfile = sandboxProfile;
        _state.State.Status = "registered";
        _state.State.UpdatedAt = DateTimeOffset.UtcNow;

        await _state.WriteStateAsync();
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(string status, DateTimeOffset? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        _state.State.Status = status;
        _state.State.ExpiresAt = expiresAt;
        _state.State.UpdatedAt = DateTimeOffset.UtcNow;

        await _state.WriteStateAsync();
    }

    private string ExtractDeviceId()
    {
        // Key format: device/{deviceId}
        var key = this.GetPrimaryKeyString();
        var prefix = "device/";
        return key.StartsWith(prefix, StringComparison.Ordinal)
            ? key[prefix.Length..]
            : key;
    }

    /// <inheritdoc />
    public Task<DeviceSnapshot> GetSnapshotAsync()
    {
        var s = _state.State;
        return Task.FromResult(new DeviceSnapshot(
            DeviceId: ExtractDeviceId(),
            TenantId: s.TenantId,
            BotId: s.BotId,
            TemplateUuid: s.TemplateUuid,
            SandboxProfile: s.SandboxProfile,
            Status: s.Status,
            SandboxId: s.SandboxId,
            InternalEndpoint: s.InternalEndpoint,
            CreatedAt: s.CreatedAt,
            ExpiresAt: s.ExpiresAt,
            UpdatedAt: s.UpdatedAt));
    }
}
