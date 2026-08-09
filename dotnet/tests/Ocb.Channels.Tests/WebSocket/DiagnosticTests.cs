using System.Net.WebSockets;
using System.Text;
using Ocb.Channels.WebSocket;

namespace Ocb.Channels.Tests.WebSocket;

public sealed class DiagnosticTests
{
    [Fact]
    public async Task CompletedChannelWithQueuedItemsCanBeReadByRelay()
    {
        // Verify upstream→client relay reads queued items from a completed channel.
        var socket = new FakeWebSocket();
        socket.EnqueueClose();

        var upstream = new FakeUpstreamDuplex();
        upstream.WriteInbound(Encoding.UTF8.GetBytes("A"));
        upstream.WriteInbound(Encoding.UTF8.GetBytes("B"));
        upstream.WriteInbound(Encoding.UTF8.GetBytes("C"));
        upstream.CompleteInbound();

        var options = new WebSocketChannelOptions(10, 5, 100, 65536, false);
        var session = new WebSocketChannelSession(options);

        await session.RunAsync(socket, upstream, CancellationToken.None);

        var sent = socket.SentMessages.Select(s => Encoding.UTF8.GetString(s.Data)).ToArray();
        Assert.Equal(3, sent.Length);
        Assert.Equal(["A", "B", "C"], sent);
    }

    [Fact]
    public async Task UpstreamToClientRelayReadsFromCompletedChannel()
    {
        // Minimal test: verify ReadAllAsync iterates over queued-and-completed channel.
        var channel = System.Threading.Channels.Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.Complete();

        var items = new List<int>();
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        Assert.Equal([1, 2], items);
    }
}
