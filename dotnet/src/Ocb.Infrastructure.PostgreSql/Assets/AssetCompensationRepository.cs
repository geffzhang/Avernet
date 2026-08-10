using Microsoft.EntityFrameworkCore;
using Ocb.Backend.Assets;
using Ocb.Infrastructure.PostgreSql.Baas;

namespace Ocb.Infrastructure.PostgreSql.Assets;

/// <summary>
/// Manages compensation records for temporary MinIO objects that must be
/// cleaned up after a persistence failure.
/// </summary>
public sealed class AssetCompensationRepository : IAssetCompensationRepository
{
    private readonly OcbBusinessDbContext _db;

    public AssetCompensationRepository(OcbBusinessDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task RecordTempObjectAsync(
        string tenantId, string objectKey, string state, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var entity = new TempAssetEntity
        {
            TenantId = tenantId,
            ObjectKey = objectKey,
            State = state,
        };

        _db.TempAssets.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompensationRecord>> ListPendingCompensationAsync(
        string tenantId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _db.TempAssets
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ResolvedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new CompensationRecord(x.Id, x.TenantId, x.ObjectKey, x.State, x.CreatedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task MarkResolvedAsync(long id, CancellationToken ct)
    {
        await _db.TempAssets
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.ResolvedAt, DateTimeOffset.UtcNow),
                ct);
    }
}
