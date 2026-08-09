using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Ocb.Gateway.Channels;

namespace Ocb.Gateway.Tests.Channels;

public sealed class ConnectionDirectoryLeaseServiceTests
{
    [Fact]
    public void ActiveConnectionRecordHoldsCorrectValues()
    {
        var conn = new ConnectionDirectoryLeaseService.ActiveConnection(
            "t-abc", DateTimeOffset.MaxValue);

        Assert.Equal("t-abc", conn.TenantId);
        Assert.Equal(DateTimeOffset.MaxValue, conn.Expiry);
    }

    [Fact]
    public void ServiceIsRegisteredAsBackgroundService()
    {
        // Verify the type inherits from BackgroundService so it can
        // be registered via AddHostedService in Program.cs.
        var type = typeof(ConnectionDirectoryLeaseService);
        Assert.True(type.IsAssignableTo(typeof(BackgroundService)),
            "ConnectionDirectoryLeaseService must be a BackgroundService");
    }
}
