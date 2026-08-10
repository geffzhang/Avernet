using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Baas.Entities;

[Table("baas_device", Schema = "ocb_business")]
public sealed class DeviceEntity
{
    [Key]
    [Column("device_id")]
    [MaxLength(64)]
    public string DeviceId { get; set; } = null!;

    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("bot_id")]
    [MaxLength(64)]
    public string BotId { get; set; } = null!;

    [Column("template_uuid")]
    [MaxLength(64)]
    public string TemplateUuid { get; set; } = null!;

    [Column("sandbox_profile")]
    [MaxLength(32)]
    public string SandboxProfile { get; set; } = null!;

    [Column("status")]
    [MaxLength(32)]
    public string Status { get; set; } = "creating";

    [Column("sandbox_id")]
    [MaxLength(128)]
    public string? SandboxId { get; set; }

    [Column("internal_endpoint")]
    [MaxLength(512)]
    public string? InternalEndpoint { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
