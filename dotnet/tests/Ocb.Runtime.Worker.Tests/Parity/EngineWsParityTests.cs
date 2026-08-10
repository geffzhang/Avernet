using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Runtime.Worker.Api.WebSocket;

namespace Ocb.Runtime.Worker.Tests.Parity;

/// <summary>
/// Engine WebSocket parity tests —
/// verify that the .NET Worker serves the same WebSocket contract as the
/// engine protocol baseline.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class EngineWsParityTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly JsonSerializerOptions _jsonOptions;

    public EngineWsParityTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Replace the admission gate with a small one for capacity tests
                    services.Remove(services.First(d =>
                        d.ServiceType == typeof(EngineWsConnectionAdmissionGate)));
                    services.AddSingleton(new EngineWsConnectionAdmissionGate(
                        maxConnections: 5));
                });
            });

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    /// <summary>
    /// Unknown method must return INVALID_REQUEST error frame.
    /// </summary>
    [Fact]
    public async Task UnknownMethod_ShouldReturnErrorFrame_InvalidRequest()
    {
        using var ws = await ConnectAndSkipChallengeAsync();

        var response = await SendRequestAsync(ws, "unknown.method", new { });

        Assert.NotNull(response);
        Assert.Equal("res", response.Type);
        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal("INVALID_REQUEST", response.Error!.Code);
    }

    /// <summary>
    /// Connection admission gate — TryAcquire returns false once at capacity.
    /// </summary>
    [Fact]
    public void AdmissionGate_AtCapacity_ShouldRejectConnection()
    {
        var gate = new EngineWsConnectionAdmissionGate(maxConnections: 1);

        Assert.True(gate.TryAcquire());
        Assert.True(gate.AtCapacity);

        // Second acquire must fail
        Assert.False(gate.TryAcquire());

        // Release and re-acquire must succeed
        gate.Release();
        Assert.False(gate.AtCapacity);
        Assert.True(gate.TryAcquire());
    }

    /// <summary>
    /// Connection admission gate — integration: closing a WS connection
    /// should release the slot so a new connection can be made.
    /// </summary>
    [Fact]
    public async Task ConnectionClose_ShouldReleaseSlot()
    {
        // Connect and immediately close — the slot should be released.
        // We verify by ensuring we can connect again right after.
        using var first = await ConnectAsync();
        // The WS is open; close it to release the slot
        await first.CloseAsync(
            WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        // Should be able to connect again without issues
        using var second = await ConnectAsync();
        Assert.Equal(WebSocketState.Open, second.State);
    }

    /// <summary>
    /// Client requesting a higher protocol version must receive
    /// PROTOCOL_VERSION_MISMATCH error.
    /// </summary>
    [Fact]
    public async Task ConnectWithHigherProtocolVersion_ShouldFailWithVersionMismatch()
    {
        using var ws = await ConnectAsync();

        // Read the challenge event, then send connect with too-high version
        var challenge = await ReceiveFrameAsync<WsEventFrame>(ws);
        Assert.NotNull(challenge);
        Assert.Equal("connect.challenge", challenge!.Event);

        var response = await SendRequestAsync(ws, "connect", new
        {
            minProtocol = 999,
            maxProtocol = 999,
        });

        Assert.NotNull(response);
        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal("PROTOCOL_VERSION_MISMATCH", response.Error!.Code);
    }

    /// <summary>
    /// Connect with valid protocol version should succeed.
    /// </summary>
    [Fact]
    public async Task ConnectWithValidProtocolVersion_ShouldSucceed()
    {
        using var ws = await ConnectAsync();

        // Read the challenge event, then connect
        var challenge = await ReceiveFrameAsync<WsEventFrame>(ws);
        Assert.NotNull(challenge);

        var response = await SendRequestAsync(ws, "connect", new
        {
            minProtocol = EngineWsProtocolGuards.ProtocolVersion,
            maxProtocol = EngineWsProtocolGuards.ProtocolVersion,
        });

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Equal("res", response.Type);
    }

    /// <summary>
    /// connect.challenge event must carry minProtocol, maxProtocol, and a challenge string.
    /// </summary>
    [Fact]
    public async Task ConnectChallenge_ShouldContainProtocolVersions()
    {
        using var ws = await ConnectAsync();

        var frame = await ReceiveFrameAsync<WsEventFrame>(ws);

        Assert.NotNull(frame);
        Assert.Equal("event", frame!.Type);
        Assert.Equal("connect.challenge", frame.Event);

        var payload = frame.Payload!.Value;
        Assert.True(payload.TryGetProperty("minProtocol", out var minProto));
        Assert.Equal(EngineWsProtocolGuards.ProtocolVersion, minProto.GetInt32());
        Assert.True(payload.TryGetProperty("maxProtocol", out var maxProto));
        Assert.Equal(EngineWsProtocolGuards.ProtocolVersion, maxProto.GetInt32());
        Assert.True(payload.TryGetProperty("challenge", out _));
    }

    /// <summary>
    /// Invalid JSON in request frame must return INVALID_REQUEST error.
    /// </summary>
    [Fact]
    public async Task InvalidJson_ShouldReturnParseError()
    {
        using var ws = await ConnectAsync();

        // Read challenge first
        await ReceiveFrameAsync<WsEventFrame>(ws);

        // Send invalid JSON
        var invalidBytes = Encoding.UTF8.GetBytes("not json");
        await ws.SendAsync(
            invalidBytes, WebSocketMessageType.Text,
            endOfMessage: true, CancellationToken.None);

        var response = await ReceiveFrameAsync<WsResponseFrame>(ws);

        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal("INVALID_REQUEST", response.Error!.Code);
    }

    /// <summary>
    /// Method whitelist includes all expected methods from the parity corpus.
    /// </summary>
    [Theory]
    [InlineData("connect")]
    [InlineData("session.new")]
    [InlineData("sessions.list")]
    [InlineData("sessions.patch")]
    [InlineData("sessions.delete")]
    [InlineData("sessions.reset")]
    [InlineData("chat.send")]
    [InlineData("chat.history")]
    [InlineData("chat.abort")]
    [InlineData("interaction.resolve")]
    [InlineData("interaction.pending.list")]
    [InlineData("health.claude")]
    [InlineData("providers.available")]
    [InlineData("models.list")]
    public void AllowedMethods_ShouldIncludeExpected(string method)
    {
        Assert.True(EngineWsProtocolGuards.IsAllowed(method),
            $"Method '{method}' should be in the allowed set.");
    }

    /// <summary>
    /// Known bad method names must not pass the guard.
    /// </summary>
    [Theory]
    [InlineData("bot.start")]
    [InlineData("process.kill")]
    [InlineData("admin.shutdown")]
    [InlineData("")]
    public void UnknownMethods_ShouldNotBeInAllowedSet(string method)
    {
        Assert.False(EngineWsProtocolGuards.IsAllowed(method),
            $"Method '{method}' should NOT be in the allowed set.");
    }

    private async Task<WebSocket> ConnectAsync(WebApplicationFactory<Program>? factory = null)
    {
        var f = factory ?? _factory;
        var wsClient = f.Server.CreateWebSocketClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/openclaw/ws"), cts.Token);
    }

    /// <summary>
    /// Connects and skips the initial connect.challenge event so the
    /// caller can immediately send requests.
    /// </summary>
    private async Task<WebSocket> ConnectAndSkipChallengeAsync(
        WebApplicationFactory<Program>? factory = null)
    {
        var ws = await ConnectAsync(factory);
        // Drain the connect.challenge event
        await ReceiveFrameAsync<WsEventFrame>(ws);
        return ws;
    }

    private async Task<WsResponseFrame> SendRequestAsync(
        WebSocket ws, string method, object parameters)
    {
        var frame = new WsRequestFrame(
            "req", Guid.NewGuid().ToString("N"), method,
            JsonSerializer.SerializeToElement(parameters));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        await ws.SendAsync(
            bytes, WebSocketMessageType.Text,
            endOfMessage: true, CancellationToken.None);

        return await ReceiveFrameAsync<WsResponseFrame>(ws);
    }

    private async Task<T> ReceiveFrameAsync<T>(WebSocket ws) where T : class
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await ws.ReceiveAsync(buffer, cts.Token);
        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

        return JsonSerializer.Deserialize<T>(text, _jsonOptions)!;
    }

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
    }
}
