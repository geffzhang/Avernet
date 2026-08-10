using Ocb.GrainContracts.Session;
using Orleans;

namespace Ocb.Grains.Session;

/// <summary>
/// Grain that owns the lifecycle of a bot session and its associated assets.
/// Key format: <c>session/{tenantId}/{sessionId}</c>.
/// </summary>
public sealed class SessionGrain : Grain, ISessionGrain
{
    private readonly IPersistentState<SessionGrainState> _state;

    public SessionGrain(
        [PersistentState("session-assets", "orleans-storage")]
        IPersistentState<SessionGrainState> state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public async Task RecordAssetAsync(
        string tenantId, string botId, string resourceId,
        string objectKey, long sizeBytes, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);
        ValidateTenant(tenantId);

        if (_state.State.IsClosed)
        {
            throw new InvalidOperationException("Session is closed; cannot record new assets.");
        }

        _state.State.Entries.Add(new SessionAssetEntry(resourceId, objectKey, sizeBytes, sha256));
        await _state.WriteStateAsync();
    }

    /// <inheritdoc />
    public Task<SessionAssetSnapshot> GetAssetSnapshotAsync()
    {
        return Task.FromResult(new SessionAssetSnapshot(
            Entries: [.._state.State.Entries],
            IsClosed: _state.State.IsClosed));
    }

    /// <inheritdoc />
    public async Task CloseAsync()
    {
        _state.State.IsClosed = true;
        await _state.WriteStateAsync();
    }

    private void ValidateTenant(string requestTenantId)
    {
        var key = this.GetPrimaryKeyString();
        var parts = key.Split('/');
        if (parts.Length < 3
            || !string.Equals(parts[0], "session", StringComparison.Ordinal)
            || !string.Equals(parts[1], requestTenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Tenant mismatch: grain is scoped to a different tenant than the request. " +
                $"Key='{key}', requestTenantId='{requestTenantId}'");
        }
    }
}
