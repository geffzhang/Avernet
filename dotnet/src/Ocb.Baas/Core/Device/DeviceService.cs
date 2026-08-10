using Ocb.Baas.Core.Common;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Device;
using Ocb.PluginApi.Baas.Persistence;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.Baas.Core.Device;

/// <summary>
/// Device service — creates, manages, and destroys bot device sandboxes
/// with tenant isolation and TTL expiry.
/// </summary>
public sealed class DeviceService : IDeviceServiceContract
{
    private readonly IDeviceRepository _repository;
    private readonly ITemplateRepository _templateRepository;
    private readonly ISandboxProvider _sandboxProvider;

    public DeviceService(
        IDeviceRepository repository,
        ITemplateRepository templateRepository,
        ISandboxProvider sandboxProvider)
    {
        _repository = repository;
        _templateRepository = templateRepository;
        _sandboxProvider = sandboxProvider;
    }

    public async Task<DeviceInfo> CreateDeviceAsync(CallerContext caller, CreateDeviceRequest request, CancellationToken ct = default)
    {
        var template = await _templateRepository.GetByTenantAndUuidAsync(caller.TenantId, request.SandboxProfile, ct);
        var ttlMinutes = request.TtlMinutes > 0 ? request.TtlMinutes : template?.DefaultTtlMinutes ?? 30;

        var deviceId = $"dev-{Guid.NewGuid():N}"[..20];
        var now = DateTimeOffset.UtcNow;

        var record = new DeviceRecord(
            DeviceId: deviceId,
            TenantId: caller.TenantId,
            BotId: request.BotId,
            TemplateUuid: request.SandboxProfile,
            SandboxProfile: request.SandboxProfile,
            Status: "creating",
            SandboxId: null,
            InternalEndpoint: null,
            CreatedAt: now,
            ExpiresAt: now.AddMinutes(ttlMinutes),
            UpdatedAt: now);

        await _repository.InsertAsync(record, ct);

        return new DeviceInfo(
            DeviceId: deviceId,
            BotId: request.BotId,
            Status: "creating",
            CreatedAt: now,
            ExpiresAt: record.ExpiresAt);
    }

    public async Task<DestroyResult> DestroyDeviceAsync(CallerContext caller, string deviceId, CancellationToken ct = default)
    {
        var device = await _repository.GetByIdAsync(deviceId, ct);
        if (device is null)
            return new DestroyResult(Destroyed: false, Reason: "DEVICE_NOT_FOUND");

        TenantGuard.AssertTenantMatch(caller, device.TenantId);

        await _repository.UpdateStatusAsync(deviceId, "destroying", null, ct);
        return new DestroyResult(Destroyed: true, Reason: null);
    }

    public async Task<TtlRenewResult> RenewTtlAsync(CallerContext caller, string deviceId, CancellationToken ct = default)
    {
        var device = await _repository.GetByIdAsync(deviceId, ct);
        if (device is null)
            return new TtlRenewResult(Renewed: false, NewExpiresAt: DateTimeOffset.MinValue, Reason: "DEVICE_NOT_FOUND");

        TenantGuard.AssertTenantMatch(caller, device.TenantId);

        var newExpiry = DateTimeOffset.UtcNow.AddMinutes(30);
        await _repository.UpdateStatusAsync(deviceId, device.Status, newExpiry, ct);

        return new TtlRenewResult(Renewed: true, NewExpiresAt: newExpiry, Reason: null);
    }

    public async Task<DeviceStatus> GetDeviceStatusAsync(CallerContext caller, string deviceId, CancellationToken ct = default)
    {
        var device = await _repository.GetByIdAsync(deviceId, ct);
        if (device is null)
            return new DeviceStatus(DeviceId: deviceId, Status: "not_found", LastSeenAt: DateTimeOffset.MinValue);

        TenantGuard.AssertTenantMatch(caller, device.TenantId);

        return new DeviceStatus(
            DeviceId: deviceId,
            Status: device.Status,
            LastSeenAt: device.UpdatedAt);
    }

    public async Task<InvokeHttpResult> InvokeHttpAsync(CallerContext caller, InvokeHttpRequest request, CancellationToken ct = default)
    {
        var device = await _repository.GetByIdAsync(request.DeviceId, ct);
        if (device is null)
            return new InvokeHttpResult(404, new Dictionary<string, string>(), null);

        TenantGuard.AssertTenantMatch(caller, device.TenantId);

        var sandboxRequest = new SandboxInvokeHttpRequest(
            SandboxId: device.SandboxId ?? request.DeviceId,
            Method: request.Method,
            Port: request.Port,
            Path: request.Path,
            Headers: request.Headers,
            Body: request.Body);

        var response = await _sandboxProvider.InvokeHttpAsync(caller, sandboxRequest, ct);
        return new InvokeHttpResult(response.StatusCode, response.Headers, response.Body);
    }

    public async Task<WsConnectionInfo> ResolveWsInfoAsync(CallerContext caller, ResolveWsInfoRequest request, CancellationToken ct = default)
    {
        var devices = await _repository.ListByBotAsync(caller.TenantId, request.BotId, ct);
        var device = devices.FirstOrDefault(d => d.Status == "running");

        if (device is null)
            return new WsConnectionInfo(
                DeviceId: string.Empty,
                WsEndpoint: string.Empty,
                Token: null,
                ExpiresAt: DateTimeOffset.MinValue);

        return new WsConnectionInfo(
            DeviceId: device.DeviceId,
            WsEndpoint: device.InternalEndpoint ?? $"ws://localhost:8080/device/{device.DeviceId}",
            Token: null,
            ExpiresAt: device.ExpiresAt);
    }
}
