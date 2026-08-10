using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Baas.Entities;

[Table("baas_publish", Schema = "ocb_business")]
public sealed class PublishEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public long Id { get; set; }

    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("bot_id")]
    [MaxLength(64)]
    public string BotId { get; set; } = null!;

    [Column("version")]
    [MaxLength(128)]
    public string Version { get; set; } = null!;

    [Column("status")]
    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    [Column("source_locator")]
    [MaxLength(1024)]
    public string SourceLocator { get; set; } = null!;

    [Column("changelog")]
    [MaxLength(4096)]
    public string? Changelog { get; set; }

    [Column("operator_id")]
    [MaxLength(64)]
    public string? OperatorId { get; set; }

    [Column("error_message")]
    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
