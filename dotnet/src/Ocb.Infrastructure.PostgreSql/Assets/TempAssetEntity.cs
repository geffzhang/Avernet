using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Assets;

/// <summary>
/// EF Core entity for the <c>ocb_business.temp_assets</c> table.
/// Tracks temporary MinIO objects that need compensation (cleanup) after a failure.
/// </summary>
[Table("temp_assets", Schema = "ocb_business")]
public sealed class TempAssetEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("object_key")]
    public string ObjectKey { get; set; } = null!;

    [Column("state")]
    [MaxLength(32)]
    public string State { get; set; } = "TEMP_UPLOADED";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; set; }
}
