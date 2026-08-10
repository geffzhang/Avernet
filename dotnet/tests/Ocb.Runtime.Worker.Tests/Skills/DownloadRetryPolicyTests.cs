using System.Diagnostics.CodeAnalysis;
using Ocb.Runtime.Worker.Application.Skills;

namespace Ocb.Runtime.Worker.Tests.Skills;

/// <summary>
/// Verify download retry policy produces exponential backoff delays.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class DownloadRetryPolicyTests
{
    [Fact]
    public void DefaultPolicy_HasThreeRetries()
    {
        var policy = new DownloadRetryPolicy();
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    public void GetDelay_ExponentialBackoff()
    {
        var policy = new DownloadRetryPolicy(
            maxRetries: 5, baseDelayMs: 100, maxDelayMs: 5000);

        var delay0 = policy.GetDelay(0);
        var delay1 = policy.GetDelay(1);
        var delay2 = policy.GetDelay(2);

        Assert.Equal(100, delay0.TotalMilliseconds, 0);
        Assert.Equal(200, delay1.TotalMilliseconds, 0);
        Assert.Equal(400, delay2.TotalMilliseconds, 0);
    }

    [Fact]
    public void GetDelay_ShouldNotExceedMaxDelay()
    {
        var policy = new DownloadRetryPolicy(
            maxRetries: 10, baseDelayMs: 1000, maxDelayMs: 5000);

        var delay = policy.GetDelay(5); // 2^5 * 1000 = 32000, capped at 5000

        Assert.Equal(5000, delay.TotalMilliseconds, 0);
    }

    [Fact]
    public void CustomPolicy_RespectsParameters()
    {
        var policy = new DownloadRetryPolicy(
            maxRetries: 7, baseDelayMs: 200, maxDelayMs: 3000);

        Assert.Equal(7, policy.MaxRetries);
        Assert.Equal(200, policy.GetDelay(0).TotalMilliseconds, 0);
        Assert.Equal(800, policy.GetDelay(2).TotalMilliseconds, 0);
    }
}
