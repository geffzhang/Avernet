using System.Collections.Concurrent;
using Ocb.Channels.WebSocket;
using Ocb.GrainContracts.Channels;
using Orleans.Streams;

namespace Ocb.Gateway.Channels;

/// <summary>
/// Background service that subscribes to the SMS stream for
/// outbound gateway messages and dispatches them to local
/// WebSocket sessions.
/// </summary>
public sealed class StreamDispatchHostedService : BackgroundService
{
    private readonly IClusterClient _clusterClient;
    private readonly string _gatewayInstanceId;
    private readonly ConcurrentDictionary<string, WebSocketChannelSession> _localSessions;

    public StreamDispatchHostedService(
        IClusterClient clusterClient,
        IConfiguration configuration,
        ConcurrentDictionary<string, WebSocketChannelSession> localSessions)
    {
        _clusterClient = clusterClient;
        _gatewayInstanceId = configuration["Gateway:InstanceId"]
            ?? throw new InvalidOperationException(
                "Gateway:InstanceId configuration is required.");
        _localSessions = localSessions;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var streamProvider = _clusterClient.GetStreamProvider("sms");
        var streamId = StreamId.Create("GatewayOutbound", _gatewayInstanceId);
        var stream = streamProvider.GetStream<OutboundGatewayMessage>(streamId);

        var handle = await stream.SubscribeAsync((msg, _) =>
        {
            if (_localSessions.TryGetValue(msg.ConnectionId, out var session))
            {
                return session.SendToClientAsync(msg.Payload, stoppingToken).AsTask();
            }
            return Task.CompletedTask;
        });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        await handle.UnsubscribeAsync();
    }
}
