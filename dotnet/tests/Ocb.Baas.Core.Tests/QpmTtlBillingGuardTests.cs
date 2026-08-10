using System.Diagnostics.CodeAnalysis;
using Ocb.Baas.Core.Billing;
using Ocb.Baas.Core.Qpm;
using Ocb.Contracts;

namespace Ocb.Baas.Core.Tests;

/// <summary>
/// Verify QPM rate limiting, billing quota guard, and TTL boundary control.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class QpmTtlBillingGuardTests
{
    private static CallerContext Caller => new("tenant-a", "u-1", new HashSet<string> { "user" });

    // ── QPM ──

    [Fact]
    public async Task Qpm_ShouldAllow_WhenBelowLimit()
    {
        var svc = new QpmService(limitPerMinute: 100);
        var decision = await svc.CheckRateLimitAsync(Caller, "bot-1");
        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Qpm_ShouldRecord_And_DecrementRemaining()
    {
        var svc = new QpmService(limitPerMinute: 5);
        await svc.RecordUsageAsync(Caller, "bot-1");
        await svc.RecordUsageAsync(Caller, "bot-1");

        var config = await svc.GetQpmConfigAsync(Caller);
        Assert.Equal(2, config.CurrentUsagePerMinute);
    }

    [Fact]
    public async Task Qpm_ShouldUpdate_Limit()
    {
        var svc = new QpmService(limitPerMinute: 100);
        await svc.UpdateQpmConfigAsync(Caller, new Contracts.Baas.Qpm.UpdateQpmRequest(200));

        var config = await svc.GetQpmConfigAsync(Caller);
        Assert.Equal(200, config.LimitPerMinute);
    }

    // ── Billing Quota Guard ──

    [Fact]
    public async Task BillingQuotaGuard_ShouldAllow_WhenBelowQuota()
    {
        var guard = new BillingQuotaGuard(limitPerMinute: 10);
        var result = await guard.DecideAsync("tenant-a", "bot-1");

        Assert.True(result.Allowed);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task BillingQuotaGuard_ShouldReject_WhenUsageExceedsQuota()
    {
        var guard = new BillingQuotaGuard(limitPerMinute: 3);

        // Consume all quota
        for (var i = 0; i < 3; i++)
            await guard.DecideAsync("tenant-a", "bot-1");

        var result = await guard.DecideAsync("tenant-a", "bot-1");
        Assert.False(result.Allowed);
        Assert.Equal("QUOTA_EXCEEDED", result.Code);
    }

    [Fact]
    public async Task BillingQuotaGuard_TenantScoped_Isolation()
    {
        var guard = new BillingQuotaGuard(limitPerMinute: 2);

        // Tenant-a exhausts
        await guard.DecideAsync("tenant-a", "bot-1");
        await guard.DecideAsync("tenant-a", "bot-1");

        // Tenant-b should still have quota
        var bResult = await guard.DecideAsync("tenant-b", "bot-1");
        Assert.True(bResult.Allowed);
    }
}
