namespace Ocb.Backend.Assets;

/// <summary>
/// Contract for recording temporary MinIO objects that need compensation (cleanup)
/// after a persistence failure. Implemented by infrastructure adapters.
/// </summary>
public interface IAssetCompensationRepository
{
    /// <summary>
    /// Record a temporary object that needs compensation.
    /// </summary>
    Task RecordTempObjectAsync(string tenantId, string objectKey, string state, CancellationToken ct);

    /// <summary>
    /// List unresolved compensation records for a tenant.
    /// </summary>
    Task<IReadOnlyList<CompensationRecord>> ListPendingCompensationAsync(string tenantId, CancellationToken ct);

    /// <summary>
    /// Mark a compensation record as resolved (cleaned up).
    /// </summary>
    Task MarkResolvedAsync(long id, CancellationToken ct);
}

/// <summary>
/// Projection returned by <see cref="IAssetCompensationRepository.ListPendingCompensationAsync"/>.
/// </summary>
public sealed record CompensationRecord(long Id, string TenantId, string ObjectKey, string State, DateTimeOffset CreatedAt);
