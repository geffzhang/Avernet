using System.Collections.Concurrent;
using Ocb.GrainContracts.Channels;
using Ocb.GrainContracts.GrainKeys;

namespace Ocb.Gateway.Channels;

/// <summary>
/// Background service that periodically renews leases for all
/// active WebSocket connections registered in the connection directory.
/// </summary>
public sealed class ConnectionDirectoryLeaseService : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ConcurrentDictionary<string, ActiveConnection> _activeConnections;

    private static readonly TimeSpan LeaseInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Tracks a connection that should have its lease renewed.
    /// </summary>
    public sealed record ActiveConnection(string TenantId, DateTimeOffset Expiry);

    public ConnectionDirectoryLeaseService(
        IGrainFactory grainFactory,
        ConcurrentDictionary<string, ActiveConnection> activeConnections)
    {
        _grainFactory = grainFactory;
        _activeConnections = activeConnections;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LeaseInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            foreach (var (connId, connection) in _activeConnections)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    var grain = _grainFactory.GetGrain<IConnectionDirectoryGrain>(
                        TenantGrainKey.Directory(connection.TenantId));
                    await grain.RenewLeaseAsync(
                        connId, DateTimeOffset.UtcNow + LeaseDuration);
                }
                catch (Exception)
                {
                    // If lease renewal fails, the connection will be
                    // cleaned up lazily by the grain on next query.
                }
            }
        }
    }
}
