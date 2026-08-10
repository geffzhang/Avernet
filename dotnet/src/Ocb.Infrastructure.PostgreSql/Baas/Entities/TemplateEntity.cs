using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Baas.Entities;

[Table("baas_device_template", Schema = "ocb_business")]
public sealed class TemplateEntity
{
    [Key]
    [Column("uuid")]
    [MaxLength(64)]
    public string Uuid { get; set; } = null!;

    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("name")]
    [MaxLength(256)]
    public string Name { get; set; } = null!;

    [Column("sandbox_profile")]
    [MaxLength(32)]
    public string SandboxProfile { get; set; } = null!;

    [Column("image_ref")]
    [MaxLength(512)]
    public string ImageRef { get; set; } = null!;

    [Column("default_port")]
    public int DefaultPort { get; set; } = 8080;

    [Column("default_ttl_minutes")]
    public int DefaultTtlMinutes { get; set; } = 30;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
