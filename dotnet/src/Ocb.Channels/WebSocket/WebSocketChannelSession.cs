using System.Net.WebSockets;

namespace Ocb.Channels.WebSocket;

/// <summary>
/// A bidirectional relay session between a client WebSocket and an upstream
/// duplex. Each side's completion signals the other to close cooperatively
/// so that <see cref="RunAsync"/> returns cleanly after both sides finish.
/// </summary>
public sealed class WebSocketChannelSession : IDisposable
{
    private readonly FragmentAccumulator _accumulator;
    private readonly ConnectionRateLimiter _rateLimiter;
    private readonly SerialSendGate _sendGate;
    private readonly IWebSocketEnvelopeCodec _envelopeCodec;

    public WebSocketChannelSession(WebSocketChannelOptions options)
        : this(options, CreateCodec(options)) { }

    internal WebSocketChannelSession(WebSocketChannelOptions options, IWebSocketEnvelopeCodec codec)
    {
        _accumulator = new FragmentAccumulator(options.MaxMessageBytes);
        _rateLimiter = new ConnectionRateLimiter(options.MessagesPerSecondPerConnection);
        _sendGate = new SerialSendGate();
        _envelopeCodec = codec;
    }

    public void Dispose() => _sendGate.Dispose();

    /// <summary>
    /// Run the bidirectional relay between <paramref name="clientSocket"/>
    /// and <paramref name="upstream"/>. Returns when both sides complete,
    /// with each side's completion triggering a graceful close of the other.
    /// </summary>
    public async Task RunAsync(
        System.Net.WebSockets.WebSocket clientSocket,
        IWebSocketUpstreamDuplex upstream,
        CancellationToken cancellationToken)
    {
        async Task ClientToUpstream()
        {
            try
            {
                await RelayClientToUpstream(clientSocket, upstream, cancellationToken);
            }
            finally
            {
                if (upstream is not null)
                {
                    await upstream.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "client side completed",
                        CancellationToken.None);
                }
            }
        }

        async Task UpstreamToClient()
        {
            try
            {
                await RelayUpstreamToClient(clientSocket, upstream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when the session is cancelled from outside.
            }
            finally
            {
                if (clientSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await clientSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "upstream side completed",
                        CancellationToken.None);
                }
            }
        }

        try
        {
            await Task.WhenAll(ClientToUpstream(), UpstreamToClient());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // External cancellation.
        }
    }

    private static IWebSocketEnvelopeCodec CreateCodec(WebSocketChannelOptions options)
        => options.EnableJsonEnvelope
            ? new JsonEnvelopeCodec()
            : new RawEnvelopeCodec();

    private async Task RelayClientToUpstream(
        System.Net.WebSockets.WebSocket clientSocket,
        IWebSocketUpstreamDuplex upstream,
        CancellationToken cancellationToken)
    {
        while (clientSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await _accumulator.ReadMessageAsync(clientSocket, cancellationToken);
            if (message is null) break; // Close frame

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!_rateLimiter.TryAccept(now))
            {
                const int PolicyViolation = 4408;
                await clientSocket.CloseOutputAsync(
                    (WebSocketCloseStatus)PolicyViolation,
                    "rate_limit_exceeded",
                    CancellationToken.None);
                return;
            }

            var payload = _envelopeCodec.Decode(message.Value);
            await upstream.SendAsync(payload, cancellationToken);
        }
    }

    private async Task RelayUpstreamToClient(
        System.Net.WebSockets.WebSocket clientSocket,
        IWebSocketUpstreamDuplex upstream,
        CancellationToken cancellationToken)
    {
        await foreach (var frame in upstream.ReceiveAsync(cancellationToken))
        {
            if (clientSocket.State is not WebSocketState.Open and not WebSocketState.CloseReceived) break;

            var encoded = _envelopeCodec.Encode(frame);

            await _sendGate.SendAsync(
                () => clientSocket.SendAsync(
                    encoded,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None),
                cancellationToken);
        }
    }
}
