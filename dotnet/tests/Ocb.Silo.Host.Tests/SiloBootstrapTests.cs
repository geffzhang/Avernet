using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ocb.Silo.Host.Tests;

/// <summary>
/// Verify that the Silo starts correctly with the in-memory
/// (singlebox/test) profile and exposes a readiness endpoint.
/// </summary>
public sealed class SiloBootstrapTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SiloBootstrapTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Force the test profile so we use in-memory clustering
            builder.UseSetting("OCB_PROFILE", "test");
        });
    }

    [Fact]
    public async Task HealthEndpointReturnsReady()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpointReturnsTestProfile()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("test", content);
    }

    [Fact]
    public void SiloStartupDoesNotThrowForTestProfile()
    {
        // Creating the factory triggers Program.Main indirectly.
        // If it doesn't throw, the silo configured correctly.
        _ = _factory.CreateClient();
        Assert.True(true);
    }
}
