using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ocb.Infrastructure.PostgreSql.Baas.Entities;

[Table("baas_bot_run_queue", Schema = "ocb_business")]
public sealed class BotRunQueueEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public long Id { get; set; }

    [Column("bot_id")]
    [MaxLength(64)]
    public string BotId { get; set; } = null!;

    [Column("run_type")]
    [MaxLength(32)]
    public string RunType { get; set; } = null!;

    [Column("payload")]
    public string? Payload { get; set; }

    [Column("priority")]
    public int Priority { get; set; } = 0;

    [Column("status")]
    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    [Column("attempt")]
    public int Attempt { get; set; } = 0;

    [Column("max_retries")]
    public int MaxRetries { get; set; } = 3;

    [Column("worker_id")]
    [MaxLength(64)]
    public string? WorkerId { get; set; }

    [Column("lease_token")]
    [MaxLength(256)]
    public string? LeaseToken { get; set; }

    [Column("leased_at")]
    public DateTimeOffset? LeasedAt { get; set; }

    [Column("lease_expires_at")]
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
