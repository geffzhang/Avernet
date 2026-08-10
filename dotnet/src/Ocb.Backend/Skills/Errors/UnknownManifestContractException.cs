namespace Ocb.Backend.Skills.Errors;

/// <summary>
/// Thrown when a skills layout manifest declares a contract version that is not
/// recognized by this backend. The only accepted pool version is
/// <c>skills-pool-p3-v1</c>.
/// </summary>
public sealed class UnknownManifestContractException : InvalidOperationException
{
    public UnknownManifestContractException(string declaredVersion)
        : base($"Unsupported manifest contract version '{declaredVersion}'. " +
               "The only accepted pool version is 'skills-pool-p3-v1'.")
    {
        DeclaredVersion = declaredVersion;
    }

    /// <summary>
    /// The version string that the manifest declared.
    /// </summary>
    public string DeclaredVersion { get; }
}
