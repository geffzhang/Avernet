using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ocb.Channels.WebSocket;
using Ocb.Gateway.Forwarding;
using Ocb.Gateway.Routing;
using Ocb.Gateway.WebSocket;
using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.WebSocket;

/// <summary>
/// A self-contained WebSocket test host that wires up Origin policy,
/// path guards, domain routing, and a configurable upstream connector.
/// </summary>
public sealed class GatewayWsTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly TestServer _server;
    public HttpClient Client { get; }
    public FakeWebSocketUpstreamConnector Connector { get; }

    private GatewayWsTestHost(
        IHost host,
        TestServer server,
        HttpClient client,
        FakeWebSocketUpstreamConnector connector)
    {
        _host = host;
        _server = server;
        Client = client;
        Connector = connector;
    }

    public WebSocketClient CreateWebSocketClient()
        => _server.CreateWebSocketClient();

    public static async Task<GatewayWsTestHost> BuildAsync(
        string[]? allowedOrigins = null,
        TimeSpan? upstreamHandshakeDelay = null,
        bool upstreamConnectThrows = false,
        bool upstreamThrowsTimeout = false,
        string routePrefix = "openapi/v1/collaboration",
        string routeServerName = "bcs-backend")
    {
        var connector = new FakeWebSocketUpstreamConnector(
            upstreamHandshakeDelay, upstreamConnectThrows, upstreamThrowsTimeout);

        var origins = allowedOrigins ?? ["https://allowed.example"];
        var originPolicy = new WebSocketOriginPolicy(origins);

        var domainRoute = new DomainRoute(
            "bcs",
            $"/{routePrefix}",
            routeServerName,
            ServesHttp: false,
            ServesWebSocket: true,
            Rewrite: null);

        var config = new GatewayRoutingConfig([domainRoute]);
        var domainMap = DomainMap.FromGatewayConfig(config);

        var options = new WebSocketChannelOptions(10, 5, 100, 65536, false);

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(originPolicy);
                    services.AddSingleton(domainMap);
                    services.AddSingleton<IWebSocketUpstreamConnector>(connector);
                    services.AddSingleton(options);
                    services.AddTransient<WebSocketChannelSession>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseWebSockets(new WebSocketOptions
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(30)
                    });

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/openapi/v1/collaboration/messages/ws",
                            async context =>
                            {
                                if (!context.WebSockets.IsWebSocketRequest)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    return;
                                }
                                await GatewayWebSocketEndpoint.HandleAsync(context);
                            });
                    });
                });
            })
            .StartAsync();

        var server = host.GetTestServer();
        var client = host.GetTestClient();

        return new GatewayWsTestHost(host, server, client, connector);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _server.Dispose();
        _host.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test-only upstream connector that creates an in-memory duplex.
/// </summary>
public sealed class FakeWebSocketUpstreamConnector : IWebSocketUpstreamConnector
{
    private readonly TimeSpan? _delay;
    private readonly bool _throwsOnConnect;
    private readonly bool _throwsTimeout;
    public int ConnectCallCount { get; private set; }
    public GatewayTestFakeUpstreamDuplex? LastUpstreamDuplex { get; private set; }

    public FakeWebSocketUpstreamConnector(
        TimeSpan? delay,
        bool throwsOnConnect,
        bool throwsTimeout)
    {
        _delay = delay;
        _throwsOnConnect = throwsOnConnect;
        _throwsTimeout = throwsTimeout;
    }

    public async Task<IWebSocketUpstreamDuplex> ConnectAsync(
        Uri upstreamUri,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken)
    {
        ConnectCallCount++;

        if (_throwsOnConnect)
            throw new InvalidOperationException("upstream dial failed");

        if (_throwsTimeout)
            throw new OperationCanceledException(cancellationToken);

        if (_delay is { } d && d > TimeSpan.Zero)
        {
            // Simulate a slow handshake that exceeds the timeout.
            // The caller will cancel after its own handshake timeout expires.
            try
            {
                await Task.Delay(d, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        var duplex = new GatewayTestFakeUpstreamDuplex();
        LastUpstreamDuplex = duplex;
        return duplex;
    }
}

/// <summary>
/// In-memory upstream duplex for Gateway WebSocket tests.
/// </summary>
public sealed class GatewayTestFakeUpstreamDuplex : IWebSocketUpstreamDuplex
{
    private readonly List<byte[]> _sentMessages = [];
    private readonly System.Threading.Channels.Channel<ReadOnlyMemory<byte>> _inbound =
        System.Threading.Channels.Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

    public IReadOnlyList<byte[]> SentMessages => _sentMessages;

    public void WriteInbound(byte[] data) => _inbound.Writer.TryWrite(data);
    public void CompleteInbound() => _inbound.Writer.Complete();

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _sentMessages.Add(payload.ToArray());
        await Task.CompletedTask;
    }

    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken)
        => _inbound.Reader.ReadAllAsync(cancellationToken);

    public async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        _inbound.Writer.TryComplete();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        await Task.CompletedTask;
    }
}

public sealed class GatewayWebSocketEndpointTests : IAsyncDisposable
{
    [Fact]
    public async Task RejectsDisallowedOriginWith403BeforeAccept()
    {
        await using var app = await GatewayWsTestHost.BuildAsync(
            allowedOrigins: ["https://allowed.example"]);

        var wsClient = app.CreateWebSocketClient();
        wsClient.ConfigureRequest = req =>
            req.Headers["Origin"] = "https://evil.example";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wsClient.ConnectAsync(
                new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"),
                CancellationToken.None));

        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public void HasDotSegmentDetectsTraversalSegments()
    {
        // Direct unit tests for the path guard — URI normalization prevents
        // integration testing of dot segments via WebSocketClient.

        // ".." segment is detected
        Assert.True(WebSocketPathGuard.HasDotSegment(
            "/openapi/v1/collaboration/../messages/ws"));

        // "." segment is detected
        Assert.True(WebSocketPathGuard.HasDotSegment(
            "/openapi/v1/./messages/ws"));

        // Normal path has no dot segments
        Assert.False(WebSocketPathGuard.HasDotSegment(
            "/openapi/v1/collaboration/messages/ws"));
    }

    [Fact]
    public void HasRequiredRawPrefixDetectsEncodedPrefixBypass()
    {
        // Direct unit test — TestServer doesn't populate RawTarget.
        // Aligned with Python _relay_ws.py:_required_raw_prefix (line 282-316).
        // The domain prefix must appear verbatim (not encoded) in the raw path
        // to prevent "authorised as one resource, dialled as another" attacks.

        // Encoded prefix — should be rejected
        Assert.False(WebSocketPathGuard.HasRequiredRawPrefix(
            "/openapi/v1/collaboration/messages/ws",
            "/%6Fpenapi/v1/collaboration/messages/ws",
            "openapi/v1/collaboration"));

        // Plain prefix — should be allowed
        Assert.True(WebSocketPathGuard.HasRequiredRawPrefix(
            "/openapi/v1/collaboration/messages/ws",
            "/openapi/v1/collaboration/messages/ws",
            "openapi/v1/collaboration"));

        // Empty domain prefix — always allowed
        Assert.True(WebSocketPathGuard.HasRequiredRawPrefix(
            "/path", "/path", ""));
    }

    [Fact]
    public async Task UpstreamHandshakeTimeoutClosesWithEndpointUnavailable()
    {
        // Set a very short timeout so the test doesn't wait 10 seconds.
        var original = GatewayWebSocketEndpoint.HandshakeTimeout;
        try
        {
            GatewayWebSocketEndpoint.HandshakeTimeout = TimeSpan.FromMilliseconds(100);

            await using var app = await GatewayWsTestHost.BuildAsync(
                upstreamHandshakeDelay: TimeSpan.FromMilliseconds(500));

            var wsClient = app.CreateWebSocketClient();
            wsClient.ConfigureRequest = req =>
                req.Headers["Origin"] = "https://allowed.example";

            // Handshake succeeds (101), but the upstream times out.
            var socket = await wsClient.ConnectAsync(
                new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"),
                CancellationToken.None);

            // Server closes the connection with EndpointUnavailable after timeout.
            // Read to receive the close frame.
            try
            {
                var buffer = new byte[1024];
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }

            Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, socket.CloseStatus);
        }
        finally
        {
            GatewayWebSocketEndpoint.HandshakeTimeout = original;
        }
    }

    [Fact]
    public async Task UpstreamDialFailureReturnsInternalServerErrorCloseCode()
    {
        await using var app = await GatewayWsTestHost.BuildAsync(
            upstreamConnectThrows: true);

        var wsClient = app.CreateWebSocketClient();
        wsClient.ConfigureRequest = req =>
            req.Headers["Origin"] = "https://allowed.example";

        var socket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"),
            CancellationToken.None);

        // The upgrade succeeds, but upstream dial fails.
        // Server closes with InternalServerError.
        try
        {
            var buffer = new byte[1024];
            await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // Expected — server closed the connection
        }

        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
    }

    [Fact]
    public async Task SuccessfulUpstreamConnectionRelaysBidirectionally()
    {
        await using var app = await GatewayWsTestHost.BuildAsync();

        var wsClient = app.CreateWebSocketClient();
        wsClient.ConfigureRequest = req =>
            req.Headers["Origin"] = "https://allowed.example";

        var socket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"),
            CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);

        // Send a message through the relay
        var message = Encoding.UTF8.GetBytes("hello");
        await socket.SendAsync(
            new ArraySegment<byte>(message),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

        // Close the client side
        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "done",
            CancellationToken.None);

        // Give the relay a moment to process.
        await Task.Delay(50);

        // Verify upstream received the message
        Assert.NotEmpty(app.Connector.LastUpstreamDuplex?.SentMessages ?? []);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
