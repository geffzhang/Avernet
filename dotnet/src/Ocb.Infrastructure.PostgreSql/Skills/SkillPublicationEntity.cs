using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Skills;

/// <summary>
/// EF Core entity for the <c>ocb_business.skill_publications</c> table.
/// Composite key (tenant_id, bot_id, skill_id) is defined in
/// <see cref="OcbBusinessDbContext.OnModelCreating"/>.
/// </summary>
[Table("skill_publications", Schema = "ocb_business")]
public sealed class SkillPublicationEntity
{
    [Column("tenant_id")]
    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [Column("bot_id")]
    [MaxLength(128)]
    public string BotId { get; set; } = null!;

    [Column("skill_id")]
    [MaxLength(256)]
    public string SkillId { get; set; } = null!;

    [Column("source_locator")]
    public string SourceLocator { get; set; } = null!;

    [Column("immutable_version")]
    [MaxLength(256)]
    public string ImmutableVersion { get; set; } = null!;

    [Column("scheme")]
    public int Scheme { get; set; }

    [Column("package_sha256")]
    [MaxLength(64)]
    public string PackageSha256 { get; set; } = null!;

    [Column("publication_state")]
    [MaxLength(32)]
    public string PublicationState { get; set; } = "draft";

    [Column("manifest_contract_version")]
    [MaxLength(64)]
    public string ManifestContractVersion { get; set; } = "skills-pool-p3-v1";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
