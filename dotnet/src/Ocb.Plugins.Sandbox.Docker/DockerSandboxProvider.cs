using Ocb.Contracts;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.Plugins.Sandbox.Docker;

/// <summary>
/// Docker-based sandbox provider — creates containers for bot device sandboxes.
/// Currently stubbed — full Docker API integration deferred.
/// </summary>
public sealed class DockerSandboxProvider : ISandboxProvider, ISandboxConformanceProvider
{
    private readonly string _profile = "docker";
    public string Profile => _profile;

    public Task<SandboxHandle> CreateAsync(CallerContext caller, SandboxCreateRequest request, CancellationToken ct = default)
    {
        var sandboxId = $"docker-{Guid.NewGuid():N}"[..20];
        return Task.FromResult(new SandboxHandle(
            SandboxId: sandboxId,
            BotId: request.BotId,
            InternalEndpoint: $"http://{sandboxId}:{8080}",
            CreatedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(request.TtlMinutes)));
    }

    public Task DestroyAsync(CallerContext caller, string sandboxId, CancellationToken ct = default)
    {
        // Stub: destroy container
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
