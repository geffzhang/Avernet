using Ocb.Contracts.Skills;

namespace Ocb.Backend.Skills.Activation;

/// <summary>
/// Builds the set of skills that should be materialized by the Runtime Worker.
/// Only <c>active</c> publications are included; full-repo roots and bridge
/// entries are explicitly excluded to keep the physical layout solely within
/// the Runtime Worker's ownership.
/// </summary>
public static class SkillActivationPlanner
{
    /// <summary>
    /// Build an activated-only materialization plan from publication records.
    /// Filters to records with <see cref="SkillPublicationRecord.PublicationState"/> == "active"
    /// and excludes entries whose source locator references a full repo root or bridge path.
    /// </summary>
    public static IReadOnlyList<ActivatedSkillVersion> BuildActivatedOnlyPlan(
        IEnumerable<SkillPublicationRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .Where(r => string.Equals(r.PublicationState, "active", StringComparison.OrdinalIgnoreCase))
            .Where(r => !r.Version.SourceLocator.Contains("skills-repo", StringComparison.OrdinalIgnoreCase)
                        || !r.Version.SourceLocator.EndsWith('/'))
            .Where(r => !r.Version.SourceLocator.Contains("bridge", StringComparison.OrdinalIgnoreCase))
            .Select(r => new ActivatedSkillVersion(
                r.SkillId,
                r.Version.SourceLocator,
                r.Version.ImmutableVersion,
                r.PackageSha256))
            .ToList();
    }
}
