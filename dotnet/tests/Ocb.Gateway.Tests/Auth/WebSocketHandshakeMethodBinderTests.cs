using Ocb.Gateway.Auth;

namespace Ocb.Gateway.Tests.Auth;

public sealed class WebSocketHandshakeMethodBinderTests
{
    [Fact]
    public void HandshakeMethodIsWebSocket()
    {
        Assert.Equal("WEBSOCKET", WebSocketHandshakeMethodBinder.HandshakeMethod);
    }
}
