namespace Ocb.Contracts.Baas.Qpm;

/// <summary>
/// Queries-per-minute service contract for rate limiting and billing control.
/// </summary>
public interface IQpmServiceContract
{
    /// <summary>
    /// Gets the current QPM configuration for a tenant.
    /// </summary>
    Task<QpmConfig> GetQpmConfigAsync(CallerContext caller, CancellationToken ct = default);

    /// <summary>
    /// Updates QPM configuration for a tenant.
    /// </summary>
    Task<QpmConfig> UpdateQpmConfigAsync(CallerContext caller, UpdateQpmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a request is allowed under current QPM limits.
    /// </summary>
    Task<QpmDecision> CheckRateLimitAsync(CallerContext caller, string botId, CancellationToken ct = default);

    /// <summary>
    /// Records a request for QPM tracking.
    /// </summary>
    Task RecordUsageAsync(CallerContext caller, string botId, CancellationToken ct = default);
}

public sealed record QpmConfig(
    string TenantId,
    long LimitPerMinute,
    long CurrentUsagePerMinute,
    long QuotaResetAt);

public sealed record UpdateQpmRequest(long LimitPerMinute);

public sealed record QpmDecision(
    bool Allowed,
    string? Code,
    long Remaining,
    DateTimeOffset? RetryAfter);
