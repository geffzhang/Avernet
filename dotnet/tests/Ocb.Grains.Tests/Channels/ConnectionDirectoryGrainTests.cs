using System.Net.WebSockets;
using Ocb.Contracts;
using Ocb.GrainContracts.Channels;
using Ocb.GrainContracts.GrainKeys;

namespace Ocb.Grains.Tests.Channels;

public sealed class ConnectionDirectoryGrainTests
{
    [Fact]
    public async Task RegisterAndResolveConnection()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        await grain.RegisterAsync(
            "gw-1", "conn-1",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s1", DateTimeOffset.UtcNow.AddMinutes(5));

        var route = await grain.ResolveBySessionAsync("s1");
        Assert.NotNull(route);
        Assert.Equal("gw-1", route.GatewayInstanceId);
        Assert.Equal("conn-1", route.ConnectionId);
        Assert.Equal("t1", route.TenantId);
        Assert.Equal("u1", route.SubjectId);
        Assert.Equal("s1", route.SessionId);
    }

    [Fact]
    public async Task RenewLeaseExtendsExpiry()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        await grain.RegisterAsync(
            "gw-1", "conn-1",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s1", DateTimeOffset.UtcNow.AddMinutes(1));

        var newExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
        await grain.RenewLeaseAsync("conn-1", newExpiry);

        var route = await grain.ResolveBySessionAsync("s1");
        Assert.NotNull(route);
        Assert.True(route.LeaseExpiryUtc >= newExpiry.AddSeconds(-5));
    }

    [Fact]
    public async Task RemoveConnectionRemovesRoute()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        await grain.RegisterAsync(
            "gw-1", "conn-1",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s1", DateTimeOffset.UtcNow.AddMinutes(5));

        await grain.RemoveAsync("conn-1");

        var route = await grain.ResolveBySessionAsync("s1");
        Assert.Null(route);
    }

    [Fact]
    public async Task LeaseExpiryRemovesStaleConnection()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        await grain.RegisterAsync(
            "gw-1", "conn-1",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s1", DateTimeOffset.UtcNow.AddMilliseconds(100));

        // Wait for lease to expire
        await Task.Delay(500);

        var route = await grain.ResolveBySessionAsync("s1");
        Assert.Null(route);
    }

    [Fact]
    public async Task TenantAGrainDoesNotReturnTenantBConnection()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grainA = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t-A"));
        var grainB = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t-B"));

        await grainA.RegisterAsync(
            "gw-1", "conn-a",
            new CallerContext("t-A", "u1", new HashSet<string> { "user" }),
            "s-shared", DateTimeOffset.UtcNow.AddMinutes(5));
        await grainB.RegisterAsync(
            "gw-1", "conn-b",
            new CallerContext("t-B", "u2", new HashSet<string> { "user" }),
            "s-other", DateTimeOffset.UtcNow.AddMinutes(5));

        var fromA = await grainA.ResolveBySessionAsync("s-other");
        Assert.Null(fromA);
    }

    [Fact]
    public async Task GrainStateDoesNotContainWebSocketObject()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory
            .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

        await grain.RegisterAsync(
            "gw-1", "conn-1",
            new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            "s1", DateTimeOffset.UtcNow.AddMinutes(1));

        // Verify ConnectionRoute has no WebSocket-typed properties
        var wsProperties = typeof(ConnectionRoute).GetProperties()
            .Where(p => p.PropertyType == typeof(WebSocket)
                     || (p.PropertyType.FullName?.Contains("WebSocket") ?? false))
            .ToArray();

        Assert.Empty(wsProperties);
    }
}
