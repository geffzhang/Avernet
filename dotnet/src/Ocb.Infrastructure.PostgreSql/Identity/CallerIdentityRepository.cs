using Microsoft.EntityFrameworkCore;
using Ocb.Contracts.Identity;
using Ocb.Infrastructure.PostgreSql.Baas;
using Ocb.PluginApi.Identity;

namespace Ocb.Infrastructure.PostgreSql.Identity;

/// <summary>
/// PostgreSQL implementation of <see cref="ICallerIdentityRepositoryPlugin"/>.
/// Every query includes a mandatory tenant-id predicate.
/// </summary>
public sealed class CallerIdentityRepository : ICallerIdentityRepositoryPlugin
{
    private readonly OcbBusinessDbContext _db;

    public CallerIdentityRepository(OcbBusinessDbContext db)
    {
        _db = db;
    }

    public async Task<CallerIdentityBinding?> GetCallerIdentityAsync(
        string tenantId, string botId, string subjectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var entity = await _db.CallerIdentities
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId
                  && x.BotId == botId
                  && x.SubjectId == subjectId,
                cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<CallerIdentityBinding>> ListByBotAsync(
        string tenantId, string botId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var entities = await _db.CallerIdentities
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.BotId == botId)
            .ToListAsync(cancellationToken);

        return entities.ConvertAll(static e => Map(e));
    }

    private static CallerIdentityBinding Map(CallerIdentityEntity entity) =>
        new(
            TenantId: entity.TenantId,
            BotId: entity.BotId,
            SubjectId: entity.SubjectId,
            Roles: new HashSet<string>(entity.Roles, StringComparer.Ordinal));
}
