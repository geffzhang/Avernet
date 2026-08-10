using System.Diagnostics.CodeAnalysis;
using Ocb.Contracts;
using Ocb.Contracts.Fusion;
using Ocb.GrainContracts.Fusion;
using Ocb.GrainContracts.GrainKeys;

namespace Ocb.Grains.Tests.Fusion;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class FusionJobGrainIdempotencyTests
{
    [Fact]
    public async Task ExecuteAsync_SameIdempotencyKey_ShouldReturnSameResultWithoutSecondExecution()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grainKey = TenantGrainKey.FusionJob("t1", "fusion-job-1");
        var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

        var cmd = new FusionCommand(
            IdempotencyKey: "idem-1",
            Caller: new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            Request: new FusionRequestDto(
                Question: "q1",
                Participants: ["p1"],
                DriverBotId: null,
                Mode: "fusion",
                FusionMode: "consensus",
                Options: null,
                Metadata: null,
                SessionId: null)
        );

        var first = await grain.ExecuteAsync(cmd);
        var second = await grain.ExecuteAsync(cmd);

        // Idempotent: same FusionId, only executed once
        Assert.Equal(first.FusionId, second.FusionId);
        var state = await grain.GetStateAsync();
        Assert.Equal(1, state.ExecutionCount);
        Assert.Contains("idem-1", state.CompletedByIdempotency.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_TenantMismatch_ThrowsUnauthorized()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        // Grain key belongs to t-A, but CallerContext claims t-B
        var grainKey = TenantGrainKey.FusionJob("t-A", "fusion-1");
        var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

        var cmd = new FusionCommand(
            IdempotencyKey: "idem-99",
            Caller: new CallerContext("t-B", "u1", new HashSet<string> { "user" }),
            Request: new FusionRequestDto(
                Question: "q",
                Participants: ["p1"],
                DriverBotId: null,
                Mode: "fusion",
                FusionMode: "consensus",
                Options: null,
                Metadata: null,
                SessionId: null)
        );

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => grain.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_DifferentIdempotencyKeys_ShouldExecuteMultipleTimes()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grainKey = TenantGrainKey.FusionJob("t1", "fusion-job-2");
        var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

        var cmd1 = new FusionCommand(
            IdempotencyKey: "idem-a",
            Caller: new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            Request: new FusionRequestDto(
                Question: "qA", Participants: ["p1"], DriverBotId: null,
                Mode: "fusion", FusionMode: "agent", Options: null, Metadata: null, SessionId: null)
        );

        var cmd2 = new FusionCommand(
            IdempotencyKey: "idem-b",
            Caller: new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            Request: new FusionRequestDto(
                Question: "qB", Participants: ["p1", "p2"], DriverBotId: null,
                Mode: "fusion", FusionMode: "agent", Options: null, Metadata: null, SessionId: null)
        );

        var first = await grain.ExecuteAsync(cmd1);
        var second = await grain.ExecuteAsync(cmd2);

        Assert.NotEqual(first.FusionId, second.FusionId);
        var state = await grain.GetStateAsync();
        Assert.Equal(2, state.ExecutionCount);
        Assert.True(state.CompletedByIdempotency.ContainsKey("idem-a"));
        Assert.True(state.CompletedByIdempotency.ContainsKey("idem-b"));
    }

    [Fact]
    public async Task GrainState_DoesNotContainForbiddenFrameworkTypes()
    {
        var stateType = typeof(FusionJobState);
        var forbidden = new[] { "HttpClient", "IVectorStore", "SqlConnection", "WebSocket" };

        foreach (var prop in stateType.GetProperties())
        {
            var typeName = prop.PropertyType.Name;
            foreach (var forbiddenType in forbidden)
            {
                Assert.DoesNotContain(forbiddenType, typeName);
            }
        }

        // Also verify that no properties are of a forbidden type by full name
        foreach (var prop in stateType.GetProperties())
        {
            var fullName = prop.PropertyType.FullName ?? string.Empty;
            foreach (var forbiddenType in forbidden)
            {
                Assert.DoesNotContain(forbiddenType, fullName);
            }
        }
    }

    [Fact]
    public async Task GetStateAsync_ShouldReturnStateAfterExecution()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grainKey = TenantGrainKey.FusionJob("t1", "fusion-state-test");
        var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

        var state = await grain.GetStateAsync();
        Assert.Equal(0, state.ExecutionCount);
        Assert.Empty(state.CompletedByIdempotency);

        var cmd = new FusionCommand(
            IdempotencyKey: "idem-s",
            Caller: new CallerContext("t1", "u1", new HashSet<string> { "user" }),
            Request: new FusionRequestDto(
                Question: "q", Participants: ["p1"], DriverBotId: null,
                Mode: "fusion", FusionMode: "agent", Options: null, Metadata: null, SessionId: null)
        );

        await grain.ExecuteAsync(cmd);
        state = await grain.GetStateAsync();
        Assert.Equal(1, state.ExecutionCount);
        Assert.NotNull(state.LastFusionId);
    }

    [Fact]
    public async Task ExecuteAsync_TenantMatch_ShouldSucceed()
    {
        await using var fixture = await OrleansTestFixture.StartAsync();
        var grainKey = TenantGrainKey.FusionJob("tenant-match", "fj-1");
        var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

        var cmd = new FusionCommand(
            IdempotencyKey: "idem-ok",
            Caller: new CallerContext("tenant-match", "u1", new HashSet<string> { "user" }),
            Request: new FusionRequestDto(
                Question: "hello", Participants: ["w1"], DriverBotId: null,
                Mode: "fusion", FusionMode: "agent", Options: null, Metadata: null, SessionId: null)
        );

        var result = await grain.ExecuteAsync(cmd);
        Assert.NotNull(result);
        Assert.StartsWith("fuse-", result.FusionId);
    }
}
