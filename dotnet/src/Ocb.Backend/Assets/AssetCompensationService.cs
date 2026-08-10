namespace Ocb.Backend.Assets;

/// <summary>
/// Service that records temporary MinIO objects for compensation
/// when a persistence operation fails. The compensation records are
/// later reconciled by a background cleanup job.
/// </summary>
public sealed class AssetCompensationService
{
    private readonly IAssetCompensationRepository _compensationRepo;

    public AssetCompensationService(IAssetCompensationRepository compensationRepo)
    {
        _compensationRepo = compensationRepo;
    }

    /// <summary>
    /// Record a temp object during a failed asset persistence operation,
    /// ensuring the orphaned MinIO object can be cleaned up later.
    /// </summary>
    public async Task RecordTempObjectOnFailureAsync(
        string tenantId, string objectKey, Exception? error, CancellationToken ct)
    {
        var state = error is not null ? "TEMP_UPLOADED" : "PENDING_CLEANUP";

        await _compensationRepo.RecordTempObjectAsync(tenantId, objectKey, state, ct);
    }

    /// <summary>
    /// Wraps a database operation: if a <see cref="DbUpdateException"/> or
    /// other persistence error is thrown, the temp object key is recorded
    /// for later compensation before the exception is re-thrown.
    /// </summary>
    public async Task ExecuteWithCompensationAsync(
        string tenantId,
        string tempObjectKey,
        Func<CancellationToken, Task> operation,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempObjectKey);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation(ct);
        }
        catch
        {
            await _compensationRepo.RecordTempObjectAsync(
                tenantId, tempObjectKey, "TEMP_UPLOADED", CancellationToken.None);
            throw;
        }
    }
}
