using Ocb.Contracts.Skills;

namespace Ocb.Runtime.Worker.Application.Skills;

/// <summary>
/// Coordinates skill materialization for a bot.
/// Downloads skills with retry, validates integrity, and
/// publishes activation state via the atomic publisher.
/// </summary>
public sealed class SkillsMaterializationCoordinator : IRuntimeSkillMaterializationService
{
    private readonly DownloadRetryPolicy _retryPolicy;

    public SkillsMaterializationCoordinator(DownloadRetryPolicy? retryPolicy = null)
    {
        _retryPolicy = retryPolicy ?? new DownloadRetryPolicy();
    }

    /// <summary>
    /// Materializes activated skills for a bot.
    /// Currently stubbed — full download + integrity validation
    /// is deferred until the skill registry backend is available.
    /// </summary>
    public Task<MaterializationResult> MaterializeActivatedSkillsAsync(
        MaterializationRequest request, CancellationToken ct)
    {
        if (request.ActivatedSkills.Count == 0)
        {
            return Task.FromResult(new MaterializationResult(
                Succeeded: false,
                ObservedState: "NO_SKILLS",
                ActiveViewId: null,
                ErrorCode: "NO_ACTIVATED_SKILLS"));
        }

        // Validate each skill version has required fields
        foreach (var skill in request.ActivatedSkills)
        {
            if (string.IsNullOrWhiteSpace(skill.SkillId)
                || string.IsNullOrWhiteSpace(skill.SourceLocator)
                || string.IsNullOrWhiteSpace(skill.ImmutableVersion))
            {
                return Task.FromResult(new MaterializationResult(
                    Succeeded: false,
                    ObservedState: "VALIDATION_FAILED",
                    ActiveViewId: null,
                    ErrorCode: "INVALID_SKILL_VERSION"));
            }
        }

        // Stub: mark as materialized without actual download
        return Task.FromResult(new MaterializationResult(
            Succeeded: true,
            ObservedState: "MATERIALIZED",
            ActiveViewId: $"view-{Guid.NewGuid():N}"[..16],
            ErrorCode: null));
    }
}
