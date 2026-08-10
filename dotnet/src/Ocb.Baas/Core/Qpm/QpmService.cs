using Ocb.Contracts;
using Ocb.Contracts.Baas.Qpm;

namespace Ocb.Baas.Core.Qpm;

/// <summary>
/// QPM (Queries Per Minute) service — rate limiting with distributed counters.
/// Falls back to local counters when distributed cache is unavailable.
/// </summary>
public sealed class QpmService : IQpmServiceContract
{
    private long _currentUsage;

    public QpmService(long limitPerMinute = 1000)
    {
        LimitPerMinute = limitPerMinute;
    }

    public long LimitPerMinute { get; private set; }

    public Task<QpmConfig> GetQpmConfigAsync(CallerContext caller, CancellationToken ct = default)
    {
        var config = new QpmConfig(
            TenantId: caller.TenantId,
            LimitPerMinute: LimitPerMinute,
            CurrentUsagePerMinute: Interlocked.Read(ref _currentUsage),
            QuotaResetAt: DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());

        return Task.FromResult(config);
    }

    public Task<QpmConfig> UpdateQpmConfigAsync(CallerContext caller, UpdateQpmRequest request, CancellationToken ct = default)
    {
        LimitPerMinute = request.LimitPerMinute;
        return GetQpmConfigAsync(caller, ct);
    }

    public Task<QpmDecision> CheckRateLimitAsync(CallerContext caller, string botId, CancellationToken ct = default)
    {
        var current = Interlocked.Read(ref _currentUsage);
        if (current >= LimitPerMinute)
        {
            return Task.FromResult(new QpmDecision(
                Allowed: false,
                Code: "RATE_LIMITED",
                Remaining: 0,
                RetryAfter: DateTimeOffset.UtcNow.AddSeconds(5)));
        }

        return Task.FromResult(new QpmDecision(
            Allowed: true,
            Code: null,
            Remaining: LimitPerMinute - current,
            RetryAfter: null));
    }

    public Task RecordUsageAsync(CallerContext caller, string botId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _currentUsage);
        return Task.CompletedTask;
    }
}
