using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Contracts;
using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.Composition;

public sealed class GatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly bool _registerPrincipalVerifier;

    public GatewayWebApplicationFactory(bool registerPrincipalVerifier)
    {
        _registerPrincipalVerifier = registerPrincipalVerifier;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:HttpPort"] = "5000",
                ["Gateway:MaxConnections"] = "1000",
                ["Gateway:MaxConnectionsPerIp"] = "100",
                ["Gateway:MessagesPerSecondPerConnection"] = "100",
                ["Gateway:AllowedOrigins:0"] = "https://example.com",
                ["Gateway:OrleansClusterId"] = "test-cluster",
                ["Gateway:OrleansServiceId"] = "test-service"
            });
        });

        builder.ConfigureServices(services =>
        {
            if (_registerPrincipalVerifier)
            {
                services.AddSingleton<IPrincipalTokenVerifier>(
                    new DefaultTestPrincipalVerifier());
            }
            // IAccessKeyResolver is required by PrincipalVerificationMiddleware.
            services.AddSingleton<IAccessKeyResolver>(
                new DefaultTestAccessKeyResolver());
        });
    }
}

public static class GatewayTestHost
{
    public static async Task<GatewayWebApplicationFactory> BuildAsync(bool registerPrincipalVerifier = true)
    {
        var factory = new GatewayWebApplicationFactory(registerPrincipalVerifier);
        _ = factory.CreateClient();
        return factory;
    }
}

internal sealed class DefaultTestPrincipalVerifier : IPrincipalTokenVerifier
{
    public ValueTask<CallerContext> VerifyAsync(
        string bearerToken,
        string signedPrincipalHeader,
        CancellationToken cancellationToken)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal) { "user" };
        return ValueTask.FromResult(new CallerContext("test-tenant", "test-user", roles));
    }
}

internal sealed class DefaultTestAccessKeyResolver : IAccessKeyResolver
{
    public ValueTask<AccessKeyResult?> ResolveAsync(
        string accessKeyToken,
        CancellationToken cancellationToken)
    {
        // Default: no access key match — return null (no access key identity).
        return ValueTask.FromResult<AccessKeyResult?>(null);
    }
}
