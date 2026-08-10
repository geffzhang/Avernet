namespace Ocb.Backend.Skills.Manifest;

/// <summary>
/// Describes the active layout contract for a skills pool.
/// Only <c>skills-pool-p3-v1</c> is currently accepted; unknown versions fail closed.
/// </summary>
public sealed record SkillsLayoutManifest(
    string SkillsLayoutId,
    string ActiveLayout,
    string LayoutContractVersion);
