using Microsoft.EntityFrameworkCore;
using Ocb.Contracts.Skills;
using Ocb.Infrastructure.PostgreSql.Baas;
using Ocb.PluginApi.Skills;

namespace Ocb.Infrastructure.PostgreSql.Skills;

/// <summary>
/// PostgreSQL implementation of <see cref="ISkillPublicationStorePlugin"/>.
/// Every query includes a mandatory tenant-id predicate.
/// </summary>
public sealed class SkillPublicationRepository : ISkillPublicationStorePlugin
{
    private readonly OcbBusinessDbContext _db;

    public SkillPublicationRepository(OcbBusinessDbContext db)
    {
        _db = db;
    }

    public async Task<SkillPublicationRecord?> GetBySkillAsync(
        string tenantId, string botId, string skillId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var entity = await _db.SkillPublications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId
                  && x.BotId == botId
                  && x.SkillId == skillId,
                cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<SkillPublicationRecord>> ListByBotAsync(
        string tenantId, string botId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var entities = await _db.SkillPublications
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.BotId == botId)
            .ToListAsync(cancellationToken);

        return entities.ConvertAll(static e => Map(e));
    }

    public async Task InsertAsync(
        SkillPublicationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var entity = new SkillPublicationEntity
        {
            TenantId = record.TenantId,
            BotId = record.BotId,
            SkillId = record.SkillId,
            SourceLocator = record.Version.SourceLocator,
            ImmutableVersion = record.Version.ImmutableVersion,
            Scheme = (int)record.Version.Scheme,
            PackageSha256 = record.PackageSha256,
            PublicationState = record.PublicationState,
            ManifestContractVersion = record.ManifestContractVersion,
        };

        _db.SkillPublications.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdatePublicationStateAsync(
        string tenantId, string botId, string skillId, string publicationState,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var count = await _db.SkillPublications
            .Where(x => x.TenantId == tenantId
                     && x.BotId == botId
                     && x.SkillId == skillId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.PublicationState, publicationState)
                       .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    private static SkillPublicationRecord Map(SkillPublicationEntity entity) =>
        new(
            TenantId: entity.TenantId,
            BotId: entity.BotId,
            SkillId: entity.SkillId,
            Version: new SkillVersionRef(
                entity.SourceLocator,
                entity.ImmutableVersion,
                (SkillSourceScheme)entity.Scheme),
            PackageSha256: entity.PackageSha256,
            PublicationState: entity.PublicationState,
            ManifestContractVersion: entity.ManifestContractVersion);
}
