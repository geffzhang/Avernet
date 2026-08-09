using Microsoft.Extensions.Hosting;
using Ocb.Gateway.Channels;

namespace Ocb.Gateway.Tests.Channels;

public sealed class StreamDispatchHostedServiceTests
{
    [Fact]
    public void ServiceIsRegisteredAsBackgroundService()
    {
        var type = typeof(StreamDispatchHostedService);
        Assert.True(type.IsAssignableTo(typeof(BackgroundService)),
            "StreamDispatchHostedService must be a BackgroundService");
    }
}
