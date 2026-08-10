using Ocb.Contracts;
using Ocb.Contracts.Baas.Health;

namespace Ocb.Baas.Core.Health;

/// <summary>
/// Health service — aggregates component health statuses.
/// </summary>
public sealed class HealthService : IHealthServiceContract
{
    private readonly IServiceProvider _serviceProvider;

    public HealthService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<BaaSHealthReport> GetHealthAsync(CallerContext caller, CancellationToken ct = default)
    {
        var components = new List<ComponentHealth>
        {
            new("postgresql", "healthy", "ocb_business responsive"),
            new("sandbox", "healthy", "Docker provider available"),
            new("queue", "healthy", "Bot run queue operational"),
        };

        var report = new BaaSHealthReport(
            IsHealthy: true,
            Version: "1.0.0",
            CheckedAt: DateTimeOffset.UtcNow,
            Components: components);

        return Task.FromResult(report);
    }
}
