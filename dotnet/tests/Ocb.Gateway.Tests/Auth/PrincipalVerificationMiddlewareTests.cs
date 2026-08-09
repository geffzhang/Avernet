using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ocb.Contracts;
using Ocb.Gateway.Auth;
using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.Auth;

public sealed class PrincipalVerificationMiddlewareTests
{
    [Fact]
    public async Task MissingPrincipalHeaderReturns401()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IPrincipalTokenVerifier>(
                        new FakePrincipalTokenVerifier("tenant-test"));
                    services.AddSingleton<IAccessKeyResolver>(
                        new FakeAccessKeyResolver());
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<PrincipalVerificationMiddleware>();
                    app.Run(context =>
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "jwt-ok");
        // Deliberately omit X-Avernet-Principal

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantMismatchViaAccessKeyReturns403()
    {
        var accessKeyResolver = new FakeAccessKeyResolver()
            .WithKey("ak-123", new AccessKeyResult("tenant-B", "u2",
                new HashSet<string>(StringComparer.Ordinal)));

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IPrincipalTokenVerifier>(
                        new FakePrincipalTokenVerifier("tenant-A"));
                    services.AddSingleton<IAccessKeyResolver>(accessKeyResolver);
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<PrincipalVerificationMiddleware>();
                    app.Run(context =>
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "jwt-valid");
        req.Headers.TryAddWithoutValidation("X-Avernet-Principal", "signed.jwt.data");
        req.Headers.TryAddWithoutValidation("X-Avernet-Access-Key", "ak-123");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WebSocketAuthExemptionDoesNotLeakToHttpGet()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IPrincipalTokenVerifier>(
                        new FakePrincipalTokenVerifier("tenant-test"));
                    services.AddSingleton<IAccessKeyResolver>(
                        new FakeAccessKeyResolver());
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<PrincipalVerificationMiddleware>();
                    app.Run(context =>
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        // HTTP GET to a WS path: must require identity (does NOT get WS exemption)
        var response = await client.GetAsync("/openapi/v1/collaboration/messages/ws");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecretResolverReturnsCredentialForUpstreamDial()
    {
        var resolver = new FakeSecretResolver().WithSecret("bcs-access-key", "sk-abc123");
        var result = await resolver.ResolveAsync("bcs-access-key", CancellationToken.None);

        Assert.Equal("sk-abc123", result);
        // Verify secret does not appear in log output
        Assert.DoesNotContain("sk-abc123", FakeSecretResolver.LastLogOutput);
    }
}
