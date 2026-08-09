using System.Collections.Concurrent;
using Ocb.Contracts;
using Ocb.Gateway.Channels;
using Ocb.GrainContracts.Channels;
using Ocb.GrainContracts.GrainKeys;

namespace Ocb.Grains.Tests.Channels;

/// <summary>
/// Integration tests that verify the ConnectionDirectoryLeaseService
/// works correctly against a real Orleans TestCluster.
/// </summary>
public sealed class LeaseServiceIntegrationTests
{
    [Fact]
    public async Task LeaseServiceStartsAndStopsWithoutError()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var connections = new ConcurrentDictionary<string,
            ConnectionDirectoryLeaseService.ActiveConnection>();

        var service = new ConnectionDirectoryLeaseService(
            fixture.GrainFactory, connections);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately — loop exits before first delay
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RegisterAndRenewWorksThroughGrain()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        // Register a connection with a short lease
        var initialExpiry = DateTimeOffset.UtcNow.AddSeconds(1);
        await grain.RegisterAsync(
            "gw-1", "conn-1",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s1", initialExpiry);

        // Verify it's registered
        var route = await grain.ResolveBySessionAsync("s1");
        Assert.NotNull(route);

        // Renew the lease
        var newExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
        await grain.RenewLeaseAsync("conn-1", newExpiry);

        // Verify the lease was extended
        route = await grain.ResolveBySessionAsync("s1");
        Assert.NotNull(route);
        Assert.True(route.LeaseExpiryUtc > initialExpiry,
            "Lease should have been extended");
    }

    [Fact]
    public async Task RemoveGrainCleansUpConnection()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        await grain.RegisterAsync(
            "gw-1", "conn-2",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s2", DateTimeOffset.UtcNow.AddMinutes(5));

        await grain.RemoveAsync("conn-2");

        var route = await grain.ResolveBySessionAsync("s2");
        Assert.Null(route);
    }
}
