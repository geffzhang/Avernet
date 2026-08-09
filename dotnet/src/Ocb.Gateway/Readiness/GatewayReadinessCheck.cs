using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ocb.PluginApi;

namespace Ocb.Gateway.Readiness;

public sealed class GatewayReadinessCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public GatewayReadinessCheck(IServiceProvider services) => _services = services;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var verifier = _services.GetService<IPrincipalTokenVerifier>();
        return Task.FromResult(verifier is null
            ? HealthCheckResult.Unhealthy("principal verifier missing")
            : HealthCheckResult.Healthy());
    }
}
