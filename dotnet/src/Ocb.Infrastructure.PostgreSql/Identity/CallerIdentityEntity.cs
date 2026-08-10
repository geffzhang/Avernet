using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Identity;

/// <summary>
/// EF Core entity for the <c>ocb_business.caller_identities</c> table.
/// Composite key (tenant_id, bot_id, subject_id) is defined in
/// <see cref="OcbBusinessDbContext.OnModelCreating"/>.
/// </summary>
[Table("caller_identities", Schema = "ocb_business")]
public sealed class CallerIdentityEntity
{
    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("bot_id")]
    [MaxLength(128)]
    public string BotId { get; set; } = null!;

    [Column("subject_id")]
    [MaxLength(256)]
    public string SubjectId { get; set; } = null!;

    [Column("roles")]
    public string[] Roles { get; set; } = [];

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
