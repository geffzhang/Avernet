using System.Diagnostics.CodeAnalysis;
using Ocb.GrainContracts.Bot;

namespace Ocb.Grains.Tests;

/// <summary>
/// Verifies that BotGrain enforces tenant isolation on every reconciliation command.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BotGrainTenantIsolationTests
{
    [Fact]
    public async Task BotGrain_RejectsCrossTenantActivationRequest()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<IBotGrain>("bot/tenant-a/bot-1");

        var request = new BotSkillReconciliationCommand(
            TenantId: "tenant-b",
            BotId: "bot-1",
            SubjectId: "u1",
            SkillIds: ["s1"],
            ManifestContractVersion: "skills-pool-p3-v1");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grain.ReconcileSkillsAsync(request));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BotGrain_AcceptsMatchingTenant()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<IBotGrain>("bot/tenant-a/bot-1");

        var request = new BotSkillReconciliationCommand(
            TenantId: "tenant-a",
            BotId: "bot-1",
            SubjectId: "u1",
            SkillIds: ["s1", "s2"],
            ManifestContractVersion: "skills-pool-p3-v1");

        await grain.ReconcileSkillsAsync(request);

        var snapshot = await grain.GetSkillsSnapshotAsync();
        Assert.Equal(2, snapshot.DesiredSkillIds.Count);
        Assert.Contains("s1", snapshot.DesiredSkillIds);
        Assert.Contains("s2", snapshot.DesiredSkillIds);
    }

    [Fact]
    public async Task BotGrain_SnapshotReflectsReconciledState()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = fixture.GrainFactory.GetGrain<IBotGrain>("bot/tenant-a/bot-1");

        var request = new BotSkillReconciliationCommand(
            TenantId: "tenant-a",
            BotId: "bot-1",
            SubjectId: "u1",
            SkillIds: ["s1", "s2", "s3"],
            ManifestContractVersion: "skills-pool-p3-v1");

        await grain.ReconcileSkillsAsync(request);

        var snapshot = await grain.GetSkillsSnapshotAsync();
        Assert.Equal(3, snapshot.DesiredSkillIds.Count);
        Assert.Equal("reconciling", snapshot.State);
    }
}
