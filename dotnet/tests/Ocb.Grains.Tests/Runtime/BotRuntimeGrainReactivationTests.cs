using System.Diagnostics.CodeAnalysis;
using Ocb.GrainContracts.Runtime;

namespace Ocb.Grains.Tests.Runtime;

/// <summary>
/// Verifies BotRuntimeGrain reactivation after worker loss:
/// reassign scheduling, state transitions during failover.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BotRuntimeGrainReactivationTests
{
    private static string GrainKey(string botId) => $"bot:{botId}";

    private static IBotRuntimeGrain GetGrain(OrleansTestFixture fixture, string botId) =>
        fixture.GrainFactory.GetGrain<IBotRuntimeGrain>(GrainKey(botId));

    [Fact]
    public async Task OnWorkerLeaseLost_ShouldScheduleReassignDuringReactivation()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-reactivate");

        // Set up initial state
        var desired = new DesiredRuntimeState(
            "bot-reactivate", "worker-a", "running", "1.0");

        await grain.SetDesiredAsync(desired);

        var observed = new ObservedRuntimeState(
            "bot-reactivate", "worker-a", "running",
            Pid: 1000, Port: 8080, HealthEndpoint: "/health",
            ObservedAt: DateTimeOffset.UtcNow);

        await grain.ReportObservedAsync(observed);

        // Worker failure detected
        await grain.OnWorkerLeaseLostAsync("worker-a", "lease-abc");

        var snapshot = await grain.GetSnapshotAsync();

        Assert.NotNull(snapshot.Desired);
        Assert.Null(snapshot.Desired!.WorkerId);
        Assert.Equal("ReassignPending", snapshot.Desired.Status);
    }

    [Fact]
    public async Task FullFailoverFlow_WorkerLoss_NewWorker_Recovery()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-failover");

        // 1. Initial assignment to worker-a
        await grain.SetDesiredAsync(new DesiredRuntimeState(
            "bot-failover", "worker-a", "running", "1.0"));

        await grain.ReportObservedAsync(new ObservedRuntimeState(
            "bot-failover", "worker-a", "running",
            1001, 8081, "/health", DateTimeOffset.UtcNow));

        // 2. Worker-a fails
        await grain.OnWorkerLeaseLostAsync("worker-a", "lease-1");

        var afterLoss = await grain.GetSnapshotAsync();
        Assert.Null(afterLoss.Desired!.WorkerId);
        Assert.Equal("ReassignPending", afterLoss.Desired.Status);

        // 3. Coordinator reassigns to worker-b
        await grain.SetDesiredAsync(new DesiredRuntimeState(
            "bot-failover", "worker-b", "running", "1.0"));

        // 4. Worker-b reports observed state
        await grain.ReportObservedAsync(new ObservedRuntimeState(
            "bot-failover", "worker-b", "running",
            2001, 8082, "/health", DateTimeOffset.UtcNow));

        var recovered = await grain.GetSnapshotAsync();
        Assert.Equal("worker-b", recovered.Desired!.WorkerId);
        Assert.Equal("worker-b", recovered.Observed!.WorkerId);
        Assert.Equal(2001, recovered.Observed.Pid);
    }

    [Fact]
    public async Task OnWorkerLeaseLost_OnlyAffectsDesiredWorker()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-scoped");

        await grain.SetDesiredAsync(new DesiredRuntimeState(
            "bot-scoped", "worker-a", "running", "1.0"));

        // Worker-b loss should not affect this bot (different worker)
        await grain.OnWorkerLeaseLostAsync("worker-b", "lease-other");

        var snapshot = await grain.GetSnapshotAsync();
        Assert.NotNull(snapshot.Desired);
        // The workerId might be cleared because the code checks
        // `_state.State.Desired?.WorkerId == workerId` — if worker-b
        // doesn't match, it won't clear. Let's verify.
        // Actually, looking at the grain code, it only clears desired.WorkerId
        // if `_state.State.Desired?.WorkerId == workerId`, so for a different
        // worker, the desired state should remain unchanged.
        Assert.Equal("worker-a", snapshot.Desired!.WorkerId);
    }
}
