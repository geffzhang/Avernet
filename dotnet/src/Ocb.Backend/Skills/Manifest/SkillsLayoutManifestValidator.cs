namespace Ocb.Backend.Skills.Manifest;

using Ocb.Backend.Skills.Errors;

/// <summary>
/// Validates that a skills layout manifest declares a supported contract version.
/// Unknown versions are rejected fail-closed.
/// </summary>
public static class SkillsLayoutManifestValidator
{
    /// <summary>
    /// The only accepted pool layout contract version.
    /// </summary>
    public const string SupportedPoolContractVersion = "skills-pool-p3-v1";

    /// <summary>
    /// Validates the manifest and throws <see cref="UnknownManifestContractException"/>
    /// when the contract version is not recognised.
    /// </summary>
    public static void ValidateOrThrow(SkillsLayoutManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.ActiveLayout == "pool"
            && !string.Equals(manifest.LayoutContractVersion, SupportedPoolContractVersion, StringComparison.Ordinal))
        {
            throw new UnknownManifestContractException(manifest.LayoutContractVersion);
        }
    }
}
