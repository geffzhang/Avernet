namespace Ocb.Contracts.Baas.Health;

/// <summary>
/// Health check service contract for BaaS domain.
/// </summary>
public interface IHealthServiceContract
{
    /// <summary>
    /// Returns the health status of all BaaS dependencies.
    /// </summary>
    Task<BaaSHealthReport> GetHealthAsync(CallerContext caller, CancellationToken ct = default);
}

public sealed record BaaSHealthReport(
    bool IsHealthy,
    string Version,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ComponentHealth> Components);

public sealed record ComponentHealth(
    string Component,
    string Status,
    string? Detail);
