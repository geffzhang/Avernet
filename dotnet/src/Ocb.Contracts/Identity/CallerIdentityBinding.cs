using System.Text.Json.Serialization;

namespace Ocb.Contracts.Identity;

/// <summary>
/// Binding that ties a caller identity to a specific tenant, bot, and subject.
/// </summary>
public sealed record CallerIdentityBinding(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("bot_id")] string BotId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("roles")] IReadOnlySet<string> Roles
);
