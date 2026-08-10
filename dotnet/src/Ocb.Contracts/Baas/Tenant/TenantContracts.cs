namespace Ocb.Contracts.Baas.Tenant;

/// <summary>
/// Tenant service contract for managing tenant metadata.
/// </summary>
public interface ITenantServiceContract
{
    /// <summary>
    /// Gets tenant metadata. The caller may only access their own tenant.
    /// </summary>
    Task<TenantDto> GetTenantAsync(CallerContext caller, CancellationToken ct = default);

    /// <summary>
    /// Updates tenant metadata.
    /// </summary>
    Task<TenantDto> UpdateTenantAsync(CallerContext caller, UpdateTenantRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies that a tenant is active and in good standing.
    /// </summary>
    Task<TenantVerification> VerifyTenantAsync(CallerContext caller, CancellationToken ct = default);
}

public sealed record TenantDto(
    string TenantId,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    int MaxBots,
    int MaxDevices,
    long QuotaPerMinute);

public sealed record UpdateTenantRequest(
    string? DisplayName,
    int? MaxBots,
    int? MaxDevices,
    long? QuotaPerMinute);

public sealed record TenantVerification(
    bool Active,
    string? Reason,
    int CurrentBotCount,
    int CurrentDeviceCount);
