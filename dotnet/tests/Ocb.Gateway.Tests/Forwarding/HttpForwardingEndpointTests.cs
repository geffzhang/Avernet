using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ocb.Contracts;
using Ocb.Gateway.Forwarding;
using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.Forwarding;

public sealed class HttpForwardingEndpointTests
{
    [Fact]
    public async Task ReplacesInboundPrincipalHeaderWithSignedValue()
    {
        await using var host = await GatewayForwardingTestHost.BuildAsync(
            signedToken: "signed.jwt.token");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
        req.Headers.TryAddWithoutValidation("X-Avernet-Principal", "forged");
        var response = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(host.Forwarder.LastRequest);
        Assert.True(host.Forwarder.LastRequest!.Headers.TryGetValue("X-Avernet-Principal", out var signed));
        Assert.Equal("signed.jwt.token", signed);
    }

    [Fact]
    public async Task HostHeaderIsStrippedBeforeForwarding()
    {
        await using var host = await GatewayForwardingTestHost.BuildAsync(
            signedToken: "signed.jwt.token");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
        req.Headers.TryAddWithoutValidation("Host", "evil.example");
        var response = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(host.Forwarder.LastRequest);
        Assert.False(host.Forwarder.LastRequest!.Headers.ContainsKey("Host"));
    }

    [Fact]
    public async Task SseBodyIsStreamedWithoutBufferingToCompletion()
    {
        var chunks = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("data:1\n\n"),
            Encoding.UTF8.GetBytes("data:2\n\n")
        };

        await using var host = await GatewayForwardingTestHost.BuildAsync(
            bodyChunks: AsAsyncEnumerable(chunks));

        var response = await host.Client.GetAsync("/openapi/v1/chat/messages/stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("data:1\n\ndata:2\n\n", body);
    }

    [Fact]
    public async Task HopByHopHeadersAreStrippedFromResponse()
    {
        await using var host = await GatewayForwardingTestHost.BuildAsync();

        var response = await host.Client.GetAsync("/openapi/v1/bots");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Connection, Transfer-Encoding etc. must not reach the caller.
        Assert.False(response.Headers.Contains("Connection"));
        Assert.False(response.Headers.Contains("Transfer-Encoding"));
    }

    [Fact]
    public async Task ForwardRequestReceivesCorrectMethodAndPath()
    {
        await using var host = await GatewayForwardingTestHost.BuildAsync();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
        req.Headers.TryAddWithoutValidation("X-Avernet-Principal", "test-principal");
        req.Headers.TryAddWithoutValidation("X-Custom", "custom-value");

        var response = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(host.Forwarder.LastRequest);
        Assert.Equal("GET", host.Forwarder.LastRequest!.Method);
        Assert.Equal("/openapi/v1/bots", host.Forwarder.LastRequest.Path);
    }

    [Fact]
    public async Task MissingCallerContextThrowsInvalidOperationException()
    {
        // This test builds a minimal host without the CallerContext middleware
        // to verify the guard clause throws.
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s => s.AddRouting());
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(ep =>
                    {
                        ep.MapGet("/test", async ctx =>
                        {
                            await HttpForwardingEndpoint.HandleAsync(
                                ctx,
                                new FakeForwarderStub(),
                                new FakeSignerStub(),
                                new FakeFilterStub(),
                                new Uri("http://example.com/test"));
                        });
                    });
                });
            })
            .Start();

        var client = host.GetTestClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("/test"));
        Assert.Contains("CallerContext", ex.Message);
    }

    private sealed class FakeForwarderStub : IHttpForwarder
    {
        public ValueTask<ForwardResponse> ForwardAsync(ForwardRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ForwardResponse(200,
                new Dictionary<string, string>(),
                AsyncEnumerable.Empty<byte[]>()));
    }

    private sealed class FakeSignerStub : IPrincipalTokenSigner
    {
        public ValueTask<string> SignAsync(CallerContext c, string audience, CancellationToken ct)
            => ValueTask.FromResult("token");
    }

    private sealed class FakeFilterStub : IHopByHopHeaderFilter
    {
        public IReadOnlySet<string> HopByHopHeaders { get; } = new HashSet<string>();
        public bool ShouldStrip(string headerName) => false;
    }

    private static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(List<T> items)
    {
        foreach (var item in items)
            yield return item;
    }
}
