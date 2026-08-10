using System.Text.Json.Serialization;

namespace Ocb.Contracts.Skills;

/// <summary>
/// Record of a published skill version for a tenant/bot.
/// </summary>
public sealed record SkillPublicationRecord(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("bot_id")] string BotId,
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("version")] SkillVersionRef Version,
    [property: JsonPropertyName("package_sha256")] string PackageSha256,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("manifest_contract_version")] string ManifestContractVersion
);
