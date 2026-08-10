using Microsoft.EntityFrameworkCore;
using Ocb.Infrastructure.PostgreSql.Baas.Entities;
using Ocb.Infrastructure.PostgreSql.Assets;
using Ocb.Infrastructure.PostgreSql.Identity;
using Ocb.Infrastructure.PostgreSql.Skills;

namespace Ocb.Infrastructure.PostgreSql.Baas;

/// <summary>
/// EF Core DbContext for the ocb_business schema (BaaS domain + Stage-5).
/// </summary>
public sealed class OcbBusinessDbContext : DbContext
{
    public OcbBusinessDbContext(DbContextOptions<OcbBusinessDbContext> options)
        : base(options)
    {
    }

    // BaaS domain
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<TemplateEntity> Templates => Set<TemplateEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<PublishEntity> Publishes => Set<PublishEntity>();
    public DbSet<BotRunQueueEntity> BotRunQueue => Set<BotRunQueueEntity>();

    // Stage-5: Skills, Caller Identity, Assets
    public DbSet<SkillPublicationEntity> SkillPublications => Set<SkillPublicationEntity>();
    public DbSet<CallerIdentityEntity> CallerIdentities => Set<CallerIdentityEntity>();
    public DbSet<TempAssetEntity> TempAssets => Set<TempAssetEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ocb_business");

        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<TemplateEntity>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Uuid }).IsUnique();
        });

        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.BotId);
        });

        modelBuilder.Entity<PublishEntity>(entity =>
        {
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.BotId, e.Version });
        });

        modelBuilder.Entity<BotRunQueueEntity>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Priority);
        });

        // Stage-5: skill_publications composite PK
        modelBuilder.Entity<SkillPublicationEntity>(entity =>
        {
            entity.HasKey(e => new { e.TenantId, e.BotId, e.SkillId });
            entity.HasIndex(e => new { e.TenantId, e.BotId });
            entity.HasIndex(e => new { e.TenantId, e.PublicationState });
        });

        // Stage-5: caller_identities composite PK
        modelBuilder.Entity<CallerIdentityEntity>(entity =>
        {
            entity.HasKey(e => new { e.TenantId, e.BotId, e.SubjectId });
            entity.HasIndex(e => new { e.TenantId, e.BotId });
        });
    }
}
