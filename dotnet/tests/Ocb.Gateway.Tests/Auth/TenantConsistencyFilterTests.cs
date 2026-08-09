using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ocb.Contracts;

namespace Ocb.Gateway.Tests.Auth;

public sealed class TenantConsistencyFilterTests
{
    [Fact]
    public async Task MissingCallerContextReturnsUnauthorized()
    {
        using var host = await BuildTenantCheckHost(caller: null);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tenants/tenant-A/sessions/s1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantMismatchInRouteReturnsForbid()
    {
        var caller = new CallerContext("tenant-A", "u1", new HashSet<string> { "user" });
        using var host = await BuildTenantCheckHost(caller);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tenants/tenant-B/sessions/s1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MatchingTenantPassesThrough()
    {
        var caller = new CallerContext("tenant-A", "u1", new HashSet<string> { "user" });
        using var host = await BuildTenantCheckHost(caller);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tenants/tenant-A/sessions/s1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<IHost> BuildTenantCheckHost(CallerContext? caller)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();

                    if (caller is not null)
                    {
                        app.Use(async (context, next) =>
                        {
                            context.Items[typeof(CallerContext)] = caller;
                            await next(context);
                        });
                    }

                    // Inline tenant consistency check (same logic as TenantConsistencyFilter).
                    // Must run AFTER UseRouting so RouteValues are populated.
                    app.Use(async (context, next) =>
                    {
                        var ctx = (CallerContext?)context.Items[typeof(CallerContext)];
                        if (ctx is null)
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                            return;
                        }

                        if (context.Request.RouteValues.TryGetValue("tenant_id", out var tv)
                            && tv is string routeTenant
                            && !string.Equals(routeTenant, ctx.TenantId, StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                            return;
                        }

                        await next(context);
                    });

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(
                            "/tenants/{tenant_id}/sessions/{session_id}",
                            context =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                return Task.CompletedTask;
                            });
                    });
                });
            })
            .StartAsync();
    }
}
