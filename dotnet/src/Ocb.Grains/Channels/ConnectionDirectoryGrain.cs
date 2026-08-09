using Ocb.Contracts;
using Ocb.GrainContracts.Channels;

namespace Ocb.Grains.Channels;

/// <summary>
/// Tenant-scoped grain that maintains a directory of active Gateway
/// connections. Each tenant has its own directory grain keyed
/// <c>directory/{tenantId}</c>, avoiding a single-grain hotspot.
///
/// Stale connections are cleaned lazily on every
/// <see cref="ResolveBySessionAsync"/> call.
/// </summary>
public sealed class ConnectionDirectoryGrain : Grain, IConnectionDirectoryGrain
{
    private readonly IPersistentState<ConnectionDirectoryState> _state;

    public ConnectionDirectoryGrain(
        [PersistentState("connection-directory", "orleans-storage")]
        IPersistentState<ConnectionDirectoryState> state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(
        string gatewayInstanceId,
        string connectionId,
        CallerContext caller,
        string sessionId,
        DateTimeOffset leaseExpiryUtc)
    {
        _state.State.ByConnectionId[connectionId] = new ConnectionRoute(
            gatewayInstanceId,
            connectionId,
            caller.TenantId,
            caller.SubjectId,
            sessionId,
            leaseExpiryUtc);

        await _state.WriteStateAsync();
    }

    /// <inheritdoc />
    public Task RenewLeaseAsync(string connectionId, DateTimeOffset leaseExpiryUtc)
    {
        if (_state.State.ByConnectionId.TryGetValue(connectionId, out var route))
        {
            _state.State.ByConnectionId[connectionId] = route with { LeaseExpiryUtc = leaseExpiryUtc };
        }

        return _state.WriteStateAsync();
    }

    /// <inheritdoc />
    public Task RemoveAsync(string connectionId)
    {
        _state.State.ByConnectionId.Remove(connectionId);
        return _state.WriteStateAsync();
    }

    /// <inheritdoc />
    public Task<ConnectionRoute?> ResolveBySessionAsync(string sessionId)
    {
        // Lazy cleanup: remove stale connections on every query
        var now = DateTimeOffset.UtcNow;
        var staleIds = _state.State.ByConnectionId.Values
            .Where(x => x.LeaseExpiryUtc <= now)
            .Select(x => x.ConnectionId)
            .ToArray();

        foreach (var stale in staleIds)
        {
            _state.State.ByConnectionId.Remove(stale);
        }

        if (staleIds.Length > 0)
        {
            return _state.WriteStateAsync()
                .ContinueWith(_ =>
                    _state.State.ByConnectionId.Values
                        .FirstOrDefault(x => x.SessionId == sessionId),
                    TaskContinuationOptions.ExecuteSynchronously)!;
        }

        return Task.FromResult(
            _state.State.ByConnectionId.Values
                .FirstOrDefault(x => x.SessionId == sessionId));
    }
}
