using Ocb.Baas.Core.Common;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Tenant;
using Ocb.PluginApi.Baas.Persistence;

namespace Ocb.Baas.Core.Tenant;

/// <summary>
/// Tenant service — manages tenant metadata with strict tenant-scoped access.
/// </summary>
public sealed class TenantService : ITenantServiceContract
{
    private readonly ITenantRepository _repository;

    public TenantService(ITenantRepository repository)
    {
        _repository = repository;
    }

    public async Task<TenantDto> GetTenantAsync(CallerContext caller, CancellationToken ct = default)
    {
        var record = await _repository.GetByTenantIdAsync(caller.TenantId, ct);
        if (record is null)
            throw new ResourceNotFoundException("TENANT_NOT_FOUND");

        return MapToDto(record);
    }

    public async Task<TenantDto> UpdateTenantAsync(CallerContext caller, UpdateTenantRequest request, CancellationToken ct = default)
    {
        var existing = await _repository.GetByTenantIdAsync(caller.TenantId, ct);
        if (existing is null)
            throw new ResourceNotFoundException("TENANT_NOT_FOUND");

        var updated = existing with
        {
            DisplayName = request.DisplayName ?? existing.DisplayName,
            MaxBots = request.MaxBots ?? existing.MaxBots,
            MaxDevices = request.MaxDevices ?? existing.MaxDevices,
            QuotaPerMinute = request.QuotaPerMinute ?? existing.QuotaPerMinute,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpsertAsync(updated, ct);
        return MapToDto(updated);
    }

    public async Task<TenantVerification> VerifyTenantAsync(CallerContext caller, CancellationToken ct = default)
    {
        var record = await _repository.GetByTenantIdAsync(caller.TenantId, ct);
        if (record is null)
        {
            return new TenantVerification(
                Active: false,
                Reason: "TENANT_NOT_FOUND",
                CurrentBotCount: 0,
                CurrentDeviceCount: 0);
        }

        if (record.Status != "active")
        {
            return new TenantVerification(
                Active: false,
                Reason: $"TENANT_STATUS_{record.Status.ToUpperInvariant()}",
                CurrentBotCount: 0,
                CurrentDeviceCount: 0);
        }

        return new TenantVerification(
            Active: true,
            Reason: null,
            CurrentBotCount: 0,
            CurrentDeviceCount: 0);
    }

    private static TenantDto MapToDto(TenantRecord r) => new(
        TenantId: r.TenantId,
        DisplayName: r.DisplayName,
        Status: r.Status,
        CreatedAt: r.CreatedAt,
        MaxBots: r.MaxBots,
        MaxDevices: r.MaxDevices,
        QuotaPerMinute: r.QuotaPerMinute);
}
