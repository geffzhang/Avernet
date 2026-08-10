namespace Ocb.Baas.Core.Billing;

/// <summary>
/// Quota/usage billing guard — enforces usage limits without
/// implementing a complete payment product.
/// </summary>
public sealed class BillingQuotaGuard
{
    private readonly long _limitPerMinute;
    private readonly Dictionary<string, long> _usage = new();
    private readonly object _lock = new();

    public BillingQuotaGuard(long limitPerMinute = 1000)
    {
        _limitPerMinute = limitPerMinute;
    }

    public Task<QuotaDecision> DecideAsync(string tenantId, string botId, CancellationToken ct = default)
    {
        var key = $"{tenantId}:{botId}";

        lock (_lock)
        {
            _usage.TryGetValue(key, out var current);

            if (current >= _limitPerMinute)
            {
                return Task.FromResult(new QuotaDecision(
                    Allowed: false,
                    Code: "QUOTA_EXCEEDED",
                    Remaining: 0));
            }

            _usage[key] = current + 1;
            var remaining = _limitPerMinute - _usage[key];

            return Task.FromResult(new QuotaDecision(
                Allowed: true,
                Code: null,
                Remaining: (int)remaining));
        }
    }
}

public sealed record QuotaDecision(bool Allowed, string? Code, int Remaining);
