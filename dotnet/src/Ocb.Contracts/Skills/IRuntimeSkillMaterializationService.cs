namespace Ocb.Contracts.Skills;

/// <summary>
/// Contracts for runtime skill materialization —
/// Backend calls into Runtime Worker to materialize activated skills.
/// </summary>

/// <summary>
/// Identifies a specific version of a skill that must be materialized.
/// </summary>
public sealed record ActivatedSkillVersion(
    string SkillId,
    string SourceLocator,
    string ImmutableVersion,
    string ExpectedSha256);

/// <summary>
/// Caller context for the materialization request.
/// </summary>
public sealed record CallerContext(
    string TenantId,
    string SubjectId);

/// <summary>
/// Request to materialize a set of activated skills for a bot.
/// </summary>
public sealed record MaterializationRequest(
    CallerContext Caller,
    string BotId,
    string ManifestContractVersion,
    IReadOnlyList<ActivatedSkillVersion> ActivatedSkills);

/// <summary>
/// Result of a materialization attempt.
/// </summary>
public sealed record MaterializationResult(
    bool Succeeded,
    string ObservedState,
    string? ActiveViewId,
    string? ErrorCode);

/// <summary>
/// Service contract exposed to Backend for skill materialization.
/// Implemented by the Runtime Worker and invoked via service API.
/// </summary>
public interface IRuntimeSkillMaterializationService
{
    /// <summary>
    /// Downloads and activates the specified skills for the given bot.
    /// </summary>
    Task<MaterializationResult> MaterializeActivatedSkillsAsync(
        MaterializationRequest request, CancellationToken ct);
}
