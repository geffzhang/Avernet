using Ocb.Contracts;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.Plugins.Sandbox.K8s;

/// <summary>
/// Kubernetes-based sandbox provider — creates pods for bot device sandboxes.
/// Currently stubbed — full K8s API integration deferred.
/// </summary>
public sealed class K8sSandboxProvider : ISandboxProvider, ISandboxConformanceProvider
{
    private readonly string _profile = "k8s";
    public string Profile => _profile;

    public Task<SandboxHandle> CreateAsync(CallerContext caller, SandboxCreateRequest request, CancellationToken ct = default)
    {
        var sandboxId = $"k8s-{Guid.NewGuid():N}"[..20];
        return Task.FromResult(new SandboxHandle(
            SandboxId: sandboxId,
            BotId: request.BotId,
            InternalEndpoint: $"http://{sandboxId}.default.svc.cluster.local:8080",
            CreatedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(request.TtlMinutes)));
    }

    public Task DestroyAsync(CallerContext caller, string sandboxId, CancellationToken ct = default)
    {
        // Stub: delete pod
        return Task.CompletedTask;
    }

    public Task<SandboxHttpResponse> InvokeHttpAsync(CallerContext caller, SandboxInvokeHttpRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new SandboxHttpResponse(
            StatusCode: 200,
            Headers: new Dictionary<string, string>(),
            Body: null));
    }

    public Task<SandboxStatus> GetStatusAsync(CallerContext caller, string sandboxId, CancellationToken ct = default)
    {
        return Task.FromResult(new SandboxStatus(
            SandboxId: sandboxId,
            Status: "running",
            LastSeenAt: DateTimeOffset.UtcNow));
    }
}
