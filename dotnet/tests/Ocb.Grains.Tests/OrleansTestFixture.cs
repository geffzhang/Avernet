using Microsoft.Extensions.DependencyInjection;
using Ocb.Grains.Channels;
using Ocb.GrainContracts.Fusion;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Ocb.Grains.Tests;

/// <summary>
/// Provides a configured Orleans TestCluster using in-memory
/// storage for local integration tests.
/// </summary>
public sealed class OrleansTestFixture : IAsyncDisposable
{
    private readonly TestCluster _cluster;

    private OrleansTestFixture(TestCluster cluster)
    {
        _cluster = cluster;
    }

    /// <summary>
    /// The Orleans grain factory bound to the test cluster.
    /// </summary>
    public IGrainFactory GrainFactory => _cluster.GrainFactory;

    /// <summary>
    /// Start a new test cluster with in-memory grain storage.
    /// </summary>
    public static async Task<OrleansTestFixture> StartAsync()
    {
        var builder = new TestClusterBuilder(1);

        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return new OrleansTestFixture(cluster);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }
}

/// <summary>
/// Configures the test silo with in-memory storage and stub services.
/// </summary>
public sealed class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        // Force-load Ocb.Grains so its [RegisterConverter] is discovered
        // before Orleans validates serializers.
        _ = typeof(CallerContextConverter).Assembly;

        siloBuilder
            .AddMemoryGrainStorage("orleans-storage")
            .AddMemoryGrainStorage("PubSubStore")
            .AddIncomingGrainCallFilter<TenantKeyGuardCallFilter>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IFusionCoordinator, StubFusionCoordinator>();
            });
    }
}

/// <summary>
/// Stub coordinator for test scenarios. Returns a minimal fusion result.
/// </summary>
internal sealed class StubFusionCoordinator : IFusionCoordinator
{
    public Task<Ocb.Contracts.Fusion.FuseResponseDto> RunAsync(FusionCommand command, CancellationToken ct)
    {
        var participants = command.Request.Participants ?? Array.Empty<string>();
        var result = new Ocb.Contracts.Fusion.FuseResponseDto(
            GroupId: "default",
            FusionId: $"fuse-{Guid.NewGuid():N}",
            Question: command.Request.Question ?? string.Empty,
            DriverBotId: command.Request.DriverBotId,
            Perspectives: participants.Select(p => new Ocb.Contracts.Fusion.PerspectiveResponseDto(
                WorkerId: p, ParticipantId: p, Response: $"P({p})", Confidence: 0.9f, Sources: null, LatencyMs: 10)).ToList(),
            Recommendation: new Ocb.Contracts.Fusion.RecommendationResponseDto("rec", participants.Count, participants.Count, 0.95f),
            PartialSuccess: false,
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            Timing: new Ocb.Contracts.Fusion.TimingResponseDto(50, 10, 20, 5, 15),
            FusionMode: command.Request.FusionMode ?? "agent"
        );
        return Task.FromResult(result);
    }
}
