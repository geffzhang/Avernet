using System.Text.Json.Serialization;

namespace Ocb.Contracts.Skills;

/// <summary>
/// Request to activate a set of skills for a bot within a tenant.
/// </summary>
public sealed record SkillActivationRequest(
    [property: JsonPropertyName("caller")] Ocb.Contracts.CallerContext Caller,
    [property: JsonPropertyName("bot_id")] string BotId,
    [property: JsonPropertyName("skill_ids")] IReadOnlyList<string> SkillIds,
    [property: JsonPropertyName("manifest_contract_version")] string ManifestContractVersion
);
