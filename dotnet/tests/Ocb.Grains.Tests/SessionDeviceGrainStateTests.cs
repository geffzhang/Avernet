using System.Diagnostics.CodeAnalysis;
using Ocb.GrainContracts.Device;
using Ocb.GrainContracts.Session;

namespace Ocb.Grains.Tests;

/// <summary>
/// Verifies SessionGrain asset tracking and DeviceGrain lifecycle state.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SessionDeviceGrainStateTests
{
    [Fact]
    public async Task SessionGrain_RecordsAssetAndReturnsSnapshot()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<ISessionGrain>("session/t1/session-1");

        await grain.RecordAssetAsync("t1", "bot-1", "res-1",
            "tenant/t1/bots/bot-1/sessions/session-1/res-1/file.bin",
            1_048_576, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var snapshot = await grain.GetAssetSnapshotAsync();

        Assert.False(snapshot.IsClosed);
        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal("res-1", entry.ResourceId);
        Assert.Equal(1_048_576, entry.SizeBytes);
    }

    [Fact]
    public async Task SessionGrain_RejectsCrossTenant()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<ISessionGrain>("session/t1/session-1");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grain.RecordAssetAsync("t2", "bot-1", "res-1",
                "tenant/t2/key", 100, "sha256"));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionGrain_ClosedSessionRejectsNewAssets()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<ISessionGrain>("session/t1/session-1");

        await grain.RecordAssetAsync("t1", "bot-1", "res-1",
            "tenant/t1/key", 100, "sha256");
        await grain.CloseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            grain.RecordAssetAsync("t1", "bot-1", "res-2",
                "tenant/t1/key2", 200, "sha256"));

        Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeviceGrain_RegisterAndGetSnapshot()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<IDeviceGrain>("device/dev-1");

        await grain.RegisterAsync("t1", "bot-1", "tmpl-uuid", "standard");

        var snapshot = await grain.GetSnapshotAsync();

        Assert.Equal("dev-1", snapshot.DeviceId);
        Assert.Equal("t1", snapshot.TenantId);
        Assert.Equal("bot-1", snapshot.BotId);
        Assert.Equal("tmpl-uuid", snapshot.TemplateUuid);
        Assert.Equal("registered", snapshot.Status);
    }

    [Fact]
    public async Task DeviceGrain_SetStatusAndUpdateLifecycle()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<IDeviceGrain>("device/dev-2");

        await grain.RegisterAsync("t1", "bot-1", "tmpl-uuid", "standard");

        var expires = DateTimeOffset.UtcNow.AddHours(1);
        await grain.SetStatusAsync("running", expires);

        var snapshot = await grain.GetSnapshotAsync();
        Assert.Equal("running", snapshot.Status);
        Assert.NotNull(snapshot.ExpiresAt);
    }
}
