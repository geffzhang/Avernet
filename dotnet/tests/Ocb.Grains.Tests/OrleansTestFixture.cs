using Ocb.Grains.Channels;
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
/// Configures the test silo with in-memory storage.
/// </summary>
public sealed class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("orleans-storage")
            .AddMemoryGrainStorage("PubSubStore")
            .AddIncomingGrainCallFilter<TenantKeyGuardCallFilter>();
    }
}
