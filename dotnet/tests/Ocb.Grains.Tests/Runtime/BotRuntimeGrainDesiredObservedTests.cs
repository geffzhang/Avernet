using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ocb.GrainContracts.Runtime;

namespace Ocb.Grains.Tests.Runtime;

/// <summary>
/// Verifies BotRuntimeGrain desired/observed state coordination:
/// state transitions, worker loss handling, and snapshot constraints.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BotRuntimeGrainDesiredObservedTests
{
    private static string GrainKey(string botId) => $"bot:{botId}";

    private static IBotRuntimeGrain GetGrain(OrleansTestFixture fixture, string botId) =>
        fixture.GrainFactory.GetGrain<IBotRuntimeGrain>(GrainKey(botId));

    [Fact]
    public async Task SetDesiredAndReportObserved_ShouldPersist()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-1");

        var desired = new DesiredRuntimeState("bot-1", "worker-a", "running", "1.0");
        await grain.SetDesiredAsync(desired);

        var observed = new ObservedRuntimeState(
            "bot-1", "worker-a", "running",
            Pid: 1234, Port: 8080, HealthEndpoint: "/health",
            ObservedAt: DateTimeOffset.UtcNow);

        await grain.ReportObservedAsync(observed);

        var snapshot = await grain.GetSnapshotAsync();

        Assert.NotNull(snapshot.Desired);
        Assert.Equal("worker-a", snapshot.Desired!.WorkerId);
        Assert.Equal("running", snapshot.Desired.Status);
        Assert.NotNull(snapshot.Observed);
        Assert.Equal(1234, snapshot.Observed!.Pid);
        Assert.Equal(8080, snapshot.Observed.Port);
    }

    [Fact]
    public async Task OnWorkerLeaseLost_ShouldScheduleReassignDuringReactivation()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-2");

        // Set desired state with worker assignment
        var desired = new DesiredRuntimeState("bot-2", "worker-a", "running", "1.0");
        await grain.SetDesiredAsync(desired);

        // Worker lease lost
        await grain.OnWorkerLeaseLostAsync("worker-a", "lease-1");

        var snapshot = await grain.GetSnapshotAsync();

        Assert.NotNull(snapshot.Desired);
        Assert.Null(snapshot.Desired!.WorkerId);
        Assert.Equal("ReassignPending", snapshot.Desired.Status);
    }

    [Fact]
    public async Task OnWorkerLeaseLost_ThenObservedReported_ShouldClearReassign()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-3");

        await grain.SetDesiredAsync(new DesiredRuntimeState(
            "bot-3", "worker-a", "running", "1.0"));

        await grain.OnWorkerLeaseLostAsync("worker-a", "lease-1");

        // New worker picks up the bot and reports observed state
        var observed = new ObservedRuntimeState(
            "bot-3", "worker-b", "running",
            Pid: 5678, Port: 9090, HealthEndpoint: "/health",
            ObservedAt: DateTimeOffset.UtcNow);
        await grain.ReportObservedAsync(observed);

        var snapshot = await grain.GetSnapshotAsync();
        Assert.Equal("worker-b", snapshot.Observed!.WorkerId);
        Assert.Equal(5678, snapshot.Observed.Pid);
    }

    [Fact]
    public async Task GetSnapshot_WhenNoState_ReturnsNullFields()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-empty");

        var snapshot = await grain.GetSnapshotAsync();

        Assert.Null(snapshot.Desired);
        Assert.Null(snapshot.Observed);
    }

    [Fact]
    public async Task Snapshot_ShouldNotContain_ProcessHandleOrAbsoluteWorkspacePath()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grain = GetGrain(fixture, "bot-5");

        var desired = new DesiredRuntimeState("bot-5", "worker-a", "running", "1.0");
        await grain.SetDesiredAsync(desired);

        var observed = new ObservedRuntimeState(
            "bot-5", "worker-a", "running",
            Pid: 9999, Port: 8080, HealthEndpoint: "/health",
            ObservedAt: DateTimeOffset.UtcNow);
        await grain.ReportObservedAsync(observed);

        var snapshot = await grain.GetSnapshotAsync();
        var json = JsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain("ProcessHandle", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/", json, StringComparison.Ordinal);
    }
}
