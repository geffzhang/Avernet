using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Baas.Entities;

[Table("baas_tenant", Schema = "ocb_business")]
public sealed class TenantEntity
{
    [Key]
    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("display_name")]
    [MaxLength(256)]
    public string DisplayName { get; set; } = null!;

    [Column("status")]
    [MaxLength(32)]
    public string Status { get; set; } = "active";

    [Column("max_bots")]
    public int MaxBots { get; set; } = 10;

    [Column("max_devices")]
    public int MaxDevices { get; set; } = 20;

    [Column("quota_per_minute")]
    public long QuotaPerMinute { get; set; } = 1000;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
