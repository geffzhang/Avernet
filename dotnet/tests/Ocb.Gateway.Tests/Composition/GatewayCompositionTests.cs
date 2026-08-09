using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Ocb.Gateway.Configuration;

namespace Ocb.Gateway.Tests.Composition;

public sealed class GatewayCompositionTests
{
    private static Dictionary<string, string?> ValidConfig() => new()
    {
        ["Gateway:HttpPort"] = "8080",
        ["Gateway:MaxConnections"] = "1000",
        ["Gateway:MaxConnectionsPerIp"] = "100",
        ["Gateway:MessagesPerSecondPerConnection"] = "100",
        ["Gateway:AllowedOrigins:0"] = "https://example.com",
        ["Gateway:OrleansClusterId"] = "test-cluster",
        ["Gateway:OrleansServiceId"] = "test-service"
    };

    [Fact]
    public void RejectsUnknownConfigurationKey()
    {
        var config = ValidConfig();
        config["Gateway:UnknownKey"] = "boom";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var ex = Assert.Throws<OptionsValidationException>(
            () => { GatewayBootstrap.ValidateAndBind(configuration); });
        Assert.Contains("Unknown configuration key", ex.Message);
    }

    [Fact]
    public void RejectsNonPositivePort()
    {
        var config = ValidConfig();
        config["Gateway:HttpPort"] = "0";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var ex = Assert.Throws<OptionsValidationException>(
            () => { GatewayBootstrap.ValidateAndBind(configuration); });
        Assert.Contains("positive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingGatewaySectionThrows()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<OptionsValidationException>(
            () => { GatewayBootstrap.ValidateAndBind(configuration); });
        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessIsUnhealthyWhenRequiredPluginMissing()
    {
        await using var app = await GatewayTestHost.BuildAsync(registerPrincipalVerifier: false);
        var client = app.CreateClient();

        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessIsHealthyWhenPluginsPresent()
    {
        await using var app = await GatewayTestHost.BuildAsync(registerPrincipalVerifier: true);
        var client = app.CreateClient();

        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
