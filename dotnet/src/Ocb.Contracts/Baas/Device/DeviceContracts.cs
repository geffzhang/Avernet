namespace Ocb.Contracts.Baas.Device;

/// <summary>
/// Device service contract for managing bot device lifecycle.
/// </summary>
public interface IDeviceServiceContract
{
    /// <summary>
    /// Transparently invokes an HTTP request against a bot device sandbox.
    /// </summary>
    Task<InvokeHttpResult> InvokeHttpAsync(CallerContext caller, InvokeHttpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resolves WebSocket connection info for a bot device.
    /// </summary>
    Task<WsConnectionInfo> ResolveWsInfoAsync(CallerContext caller, ResolveWsInfoRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates a new device for the given bot.
    /// </summary>
    Task<DeviceInfo> CreateDeviceAsync(CallerContext caller, CreateDeviceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Destroys a device by its identifier.
    /// </summary>
    Task<DestroyResult> DestroyDeviceAsync(CallerContext caller, string deviceId, CancellationToken ct = default);

    /// <summary>
    /// Renews the TTL for a device.
    /// </summary>
    Task<TtlRenewResult> RenewTtlAsync(CallerContext caller, string deviceId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current device status.
    /// </summary>
    Task<DeviceStatus> GetDeviceStatusAsync(CallerContext caller, string deviceId, CancellationToken ct = default);
}

public sealed record InvokeHttpRequest(
    string DeviceId,
    string Method,
    int Port,
    string Path,
    IReadOnlyDictionary<string, string>? Headers,
    byte[]? Body);

public sealed record InvokeHttpResult(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? Body);

public sealed record ResolveWsInfoRequest(string DeviceId, string BotId);

public sealed record WsConnectionInfo(
    string DeviceId,
    string WsEndpoint,
    string? Token,
    DateTimeOffset ExpiresAt);

public sealed record CreateDeviceRequest(
    string BotId,
    string SandboxProfile,
    int TtlMinutes);

public sealed record DeviceInfo(
    string DeviceId,
    string BotId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record DestroyResult(bool Destroyed, string? Reason);

public sealed record TtlRenewResult(bool Renewed, DateTimeOffset NewExpiresAt, string? Reason);

public sealed record DeviceStatus(
    string DeviceId,
    string Status,
    DateTimeOffset LastSeenAt);
