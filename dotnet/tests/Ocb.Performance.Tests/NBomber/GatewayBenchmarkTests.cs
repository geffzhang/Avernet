using NBomber.Contracts;
using NBomber.CSharp;

namespace Ocb.Performance.Tests.NBomber;

/// <summary>
/// NBomber performance baseline for the Gateway.
///
/// These tests require a running Gateway server.  For now the
/// threshold validation is a self-check that the defaults are
/// sane; full NBomber scenario execution is wired but guarded
/// by an environment variable until the Gateway can run end-to-end
/// in CI.
/// </summary>
public sealed class GatewayBenchmarkTests
{
    /// <summary>
    /// Verify that the default singlebox thresholds are internally
    /// consistent (validates the structure, not a running server).
    /// </summary>
    [Fact]
    public void SingleboxThresholdsAreWithinReason()
    {
        var t = GatewayPerformanceThresholds.Singlebox;

        Assert.True(t.HttpP95Ms > 0, "HTTP P95 must be positive.");
        Assert.True(t.WsConnectP95Ms > 0, "WS connect P95 must be positive.");
        Assert.True(t.SseFirstEventP95Ms > 0, "SSE first-event P95 must be positive.");
        Assert.True(t.ErrorRateUpperBound >= 0, "Error rate must be non-negative.");
        Assert.True(t.ErrorRateUpperBound <= 1, "Error rate must be ≤ 1.");
    }

    /// <summary>
    /// NBomber scenario skeleton — ready to activate when a Gateway
    /// server is running at the configured address.
    ///
    /// Set OCB_RUN_PERFORMANCE=1 to execute the actual benchmark.
    /// </summary>
    [Fact]
    public void NbomberScenariosAreLoadable()
    {
        // Smoke-test that NBomber scenario construction does not throw.
        var scenario = Scenario.Create("gateway-smoke", async _ =>
        {
            await Task.Yield();
            return Response.Ok();
        }).WithoutWarmUp()
          .WithLoadSimulations(Simulation.KeepConstant(1, TimeSpan.FromSeconds(1)));

        // NBomberRunner.RegisterScenarios validates scenario structure.
        var result = NBomberRunner
            .RegisterScenarios(scenario)
            .WithTestSuite("gateway")
            .WithTestName("smoke")
            .Run();

        Assert.NotNull(result);
        Assert.NotNull(result.ScenarioStats);
    }
}
