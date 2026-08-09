using System.Net.WebSockets;
using System.Text;
using Ocb.Channels.WebSocket;

namespace Ocb.Channels.Tests.WebSocket;

public sealed class WebSocketChannelSessionTests : IAsyncDisposable
{
    [Fact]
    public async Task AggregatesReceiveFragmentsIntoSingleMessage()
    {
        var socket = new FakeWebSocket();
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("hel"), WebSocketMessageType.Text, endOfMessage: false);
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("lo"), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueClose();

        var upstream = new FakeUpstreamDuplex();
        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: false
        );

        var session = new WebSocketChannelSession(options);
        await session.RunAsync(socket, upstream, CancellationToken.None);

        Assert.Single(upstream.SentMessages);
        Assert.Equal("hello", Encoding.UTF8.GetString(upstream.SentMessages[0]));
    }

    [Fact]
    public async Task EnforcesPerConnectionRateLimit()
    {
        var socket = new FakeWebSocket();
        // Enqueue 4 messages — rate limit is 2/sec.
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("1"), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("2"), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("4"), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueClose();

        var upstream = new FakeUpstreamDuplex();
        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 2,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: false
        );

        var session = new WebSocketChannelSession(options);
        await session.RunAsync(socket, upstream, CancellationToken.None);

        // First 2 messages pass, 3rd triggers rate limit close.
        Assert.Equal(2, upstream.SentMessages.Count);
        Assert.Equal((WebSocketCloseStatus)4408, socket.SentCloseStatus);
        Assert.Contains("rate_limit_exceeded", socket.SentCloseDescription);
    }

    [Fact]
    public async Task SerializesConcurrentSendToClient()
    {
        var socket = new FakeWebSocket();
        socket.EnqueueClose();

        var upstream = new FakeUpstreamDuplex();
        // Push messages upstream→client concurrently before running.
        upstream.WriteInbound(Encoding.UTF8.GetBytes("A"));
        upstream.WriteInbound(Encoding.UTF8.GetBytes("B"));
        upstream.WriteInbound(Encoding.UTF8.GetBytes("C"));
        upstream.CompleteInbound();

        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: false
        );

        var session = new WebSocketChannelSession(options);
        // The client side closes immediately (Close received), but upstream
        // messages still arrive. The session processes sends serially.
        await session.RunAsync(socket, upstream, CancellationToken.None);

        // All 3 sent to client, in order, no interleaving.
        var sentTexts = socket.SentMessages
            .Select(s => Encoding.UTF8.GetString(s.Data))
            .ToArray();
        Assert.Equal(["A", "B", "C"], sentTexts);
    }

    [Fact]
    public async Task UpstreamCloseEndsSessionGracefully()
    {
        var socket = new FakeWebSocket();
        // Enqueue one message to confirm bidirectional relay works,
        // then close.
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueClose();

        var upstream = new FakeUpstreamDuplex();
        upstream.CompleteInbound(); // No messages coming from upstream.

        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: false
        );

        var session = new WebSocketChannelSession(options);
        await session.RunAsync(socket, upstream, CancellationToken.None);

        // Single client message forwarded.
        Assert.Single(upstream.SentMessages);
        Assert.Equal("ping", Encoding.UTF8.GetString(upstream.SentMessages[0]));
    }

    [Fact]
    public async Task ClientCloseEndsSessionGracefully()
    {
        var socket = new FakeWebSocket();
        socket.EnqueueClose(); // Client sends Close immediately.

        var upstream = new FakeUpstreamDuplex();
        upstream.CompleteInbound();

        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: false
        );

        var session = new WebSocketChannelSession(options);
        await session.RunAsync(socket, upstream, CancellationToken.None);

        // No messages forwarded — client closed before sending anything.
        Assert.Empty(upstream.SentMessages);
    }

    [Fact]
    public async Task MessageExceedingMaxSizeThrows()
    {
        var socket = new FakeWebSocket();
        // Send a message that exceeds the 10-byte limit.
        socket.EnqueueReceive(Encoding.UTF8.GetBytes("1234567890!"), WebSocketMessageType.Text, endOfMessage: true);

        var upstream = new FakeUpstreamDuplex();
        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 10,
            EnableJsonEnvelope: false
        );

        var session = new WebSocketChannelSession(options);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunAsync(socket, upstream, CancellationToken.None));
    }

    [Fact]
    public async Task JsonEnvelopeCodecEncodesAndDecodesFrames()
    {
        var socket = new FakeWebSocket();
        var sendPayload = Encoding.UTF8.GetBytes("hello");
        var envelopeJson = """{"type":"message","payload":"aGVsbG8="}""";
        socket.EnqueueReceive(Encoding.UTF8.GetBytes(envelopeJson), WebSocketMessageType.Text, endOfMessage: true);
        socket.EnqueueClose();

        var upstream = new FakeUpstreamDuplex();
        // Upstream sends a message to the client.
        upstream.WriteInbound(Encoding.UTF8.GetBytes("response"));
        upstream.CompleteInbound();

        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: true
        );

        var session = new WebSocketChannelSession(options);
        await session.RunAsync(socket, upstream, CancellationToken.None);

        // Client→upstream: envelope decoded, raw "hello" sent upstream.
        Assert.Single(upstream.SentMessages);
        Assert.Equal("hello", Encoding.UTF8.GetString(upstream.SentMessages[0]));

        // Upstream→client: raw "response" encoded in JSON envelope.
        Assert.Single(socket.SentMessages);
        var clientReceived = Encoding.UTF8.GetString(socket.SentMessages[0].Data);
        Assert.Contains("\"type\":\"message\"", clientReceived);
        Assert.Contains("\"payload\":\"", clientReceived);
    }

    [Fact]
    public async Task CancellationStopsBidiRelay()
    {
        var socket = new FakeWebSocket();
        // No messages enqueued — ReceiveAsync will spin until cancelled.
        var upstream = new FakeUpstreamDuplex();

        var options = new WebSocketChannelOptions(
            MaxConnections: 10,
            MaxConnectionsPerIp: 5,
            MessagesPerSecondPerConnection: 100,
            MaxMessageBytes: 65536,
            EnableJsonEnvelope: false
        );

        // Cancel immediately.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var session = new WebSocketChannelSession(options);
        // Should complete without throwing when cancelled.
        await session.RunAsync(socket, upstream, cts.Token);

        // Both sides should see no data.
        Assert.Empty(upstream.SentMessages);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
