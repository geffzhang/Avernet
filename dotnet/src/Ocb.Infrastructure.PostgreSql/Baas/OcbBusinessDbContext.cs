using Microsoft.EntityFrameworkCore;
using Ocb.Infrastructure.PostgreSql.Baas.Entities;

namespace Ocb.Infrastructure.PostgreSql.Baas;

/// <summary>
/// EF Core DbContext for the ocfb_business schema (BaaS domain).
/// </summary>
public sealed class OcbBusinessDbContext : DbContext
{
    public OcbBusinessDbContext(DbContextOptions<OcbBusinessDbContext> options)
        : base(options)
    {
    }

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<TemplateEntity> Templates => Set<TemplateEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<PublishEntity> Publishes => Set<PublishEntity>();
    public DbSet<BotRunQueueEntity> BotRunQueue => Set<BotRunQueueEntity>();

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
    }
}
