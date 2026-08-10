using Ocb.Contracts;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.PluginApi.Baas.Sandbox;

/// <summary>
/// Provider interface for creating, managing, and destroying sandbox environments.
/// </summary>
public interface ISandboxProvider
{
    /// <summary>
    /// Creates a new sandbox instance for a bot.
    /// </summary>
    Task<SandboxHandle> CreateAsync(CallerContext caller, SandboxCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Destroys a sandbox instance and releases its resources.
    /// </summary>
    Task DestroyAsync(CallerContext caller, string sandboxId, CancellationToken ct = default);

    /// <summary>
    /// Invokes an HTTP request inside a sandbox instance.
    /// </summary>
    Task<SandboxHttpResponse> InvokeHttpAsync(CallerContext caller, SandboxInvokeHttpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets the current status of a sandbox instance.
    /// </summary>
    Task<SandboxStatus> GetStatusAsync(CallerContext caller, string sandboxId, CancellationToken ct = default);
}

/// <summary>
/// Conformance marker — all sandbox providers must satisfy the same contract.
/// </summary>
public interface ISandboxConformanceProvider : ISandboxProvider;

public sealed record SandboxCreateRequest(
    string BotId,
    string TemplateUuid,
    string SandboxProfile,
    int TtlMinutes);

public sealed record SandboxHandle(
    string SandboxId,
    string BotId,
    string InternalEndpoint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record SandboxInvokeHttpRequest(
    string SandboxId,
    string Method,
    int Port,
    string Path,
    IReadOnlyDictionary<string, string>? Headers,
    byte[]? Body);

public sealed record SandboxHttpResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? Body);

public sealed record SandboxStatus(
    string SandboxId,
    string Status,
    DateTimeOffset LastSeenAt);
