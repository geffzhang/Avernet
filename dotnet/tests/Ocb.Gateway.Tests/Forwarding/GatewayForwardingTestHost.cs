using System.Net;
using System.Runtime.CompilerServices;
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

public sealed class GatewayForwardingTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly TestServer _server;
    public HttpClient Client { get; }
    public FakeHttpForwarder Forwarder { get; }
    public FakePrincipalTokenSigner Signer { get; }
    public FakeHopByHopHeaderFilter HeaderFilter { get; }

    private GatewayForwardingTestHost(
        IHost host,
        TestServer server,
        HttpClient client,
        FakeHttpForwarder forwarder,
        FakePrincipalTokenSigner signer,
        FakeHopByHopHeaderFilter headerFilter)
    {
        _host = host;
        _server = server;
        Client = client;
        Forwarder = forwarder;
        Signer = signer;
        HeaderFilter = headerFilter;
    }

    public static async Task<GatewayForwardingTestHost> BuildAsync(
        string signedToken = "signed.jwt.token",
        int responseStatus = (int)HttpStatusCode.OK,
        IAsyncEnumerable<byte[]>? bodyChunks = null,
        Action<ForwardRequest>? onForward = null)
    {
        var forwarder = new FakeHttpForwarder(responseStatus, bodyChunks, onForward);
        var signer = new FakePrincipalTokenSigner(signedToken);
        var headerFilter = new FakeHopByHopHeaderFilter();

        var host = await new HostBuilder()
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

                    // Inject CallerContext before the forwarding endpoint.
                    app.Use(async (ctx, next) =>
                    {
                        ctx.Items[typeof(CallerContext)] = new CallerContext(
                            "t-test", "u-test",
                            new HashSet<string>(StringComparer.Ordinal) { "user" });
                        await next(ctx);
                    });

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/openapi/v1/bots", async context =>
                        {
                            await HttpForwardingEndpoint.HandleAsync(
                                context, forwarder, signer, headerFilter,
                                new Uri("http://bots-backend/openapi/v1/bots"));
                        });

                        endpoints.MapGet("/openapi/v1/chat/messages/stream", async context =>
                        {
                            await HttpForwardingEndpoint.HandleAsync(
                                context, forwarder, signer, headerFilter,
                                new Uri("http://engine-backend/openapi/v1/chat/messages/stream"));
                        });
                    });
                });
            })
            .StartAsync();

        var server = host.GetTestServer();
        var client = host.GetTestClient();

        return new GatewayForwardingTestHost(host, server, client, forwarder, signer, headerFilter);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _server.Dispose();
        _host.Dispose();
        await Task.CompletedTask;
    }
}

public sealed class FakeHttpForwarder : IHttpForwarder
{
    private readonly int _statusCode;
    private readonly IAsyncEnumerable<byte[]>? _bodyChunks;
    private readonly Action<ForwardRequest>? _onForward;

    public ForwardRequest? LastRequest { get; private set; }

    private static async IAsyncEnumerable<byte[]> EmptyBody()
    {
        yield break;
    }

    public FakeHttpForwarder(int statusCode, IAsyncEnumerable<byte[]>? bodyChunks, Action<ForwardRequest>? onForward)
    {
        _statusCode = statusCode;
        _bodyChunks = bodyChunks;
        _onForward = onForward;
    }

    public ValueTask<ForwardResponse> ForwardAsync(ForwardRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        _onForward?.Invoke(request);

        var body = _bodyChunks ?? EmptyBody();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Content-Type"] = "application/json"
        };

        return ValueTask.FromResult(new ForwardResponse(_statusCode, headers, body));
    }
}

public sealed class FakePrincipalTokenSigner : IPrincipalTokenSigner
{
    private readonly string _token;

    public FakePrincipalTokenSigner(string token) => _token = token;

    public ValueTask<string> SignAsync(CallerContext callerContext, string audience, CancellationToken cancellationToken)
        => ValueTask.FromResult(_token);
}

public sealed class FakeHopByHopHeaderFilter : IHopByHopHeaderFilter
{
    public IReadOnlySet<string> HopByHopHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate"
    };

    public bool ShouldStrip(string headerName)
        => HopByHopHeaders.Contains(headerName);
}
