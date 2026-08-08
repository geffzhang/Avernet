namespace Ocb.Configuration;

public static class OcbPlatformOptionsValidator
{
    public static ValidationResult Validate(OcbPlatformOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var valid = options.Profile switch
        {
            DeploymentProfile.Singlebox => options.VectorProvider is VectorProvider.SonnetDb,
            DeploymentProfile.Cluster => options.VectorProvider is VectorProvider.Qdrant,
            DeploymentProfile.Test => options.VectorProvider is VectorProvider.InMemory,
            _ => false
        };

        return valid
            ? ValidationResult.Success
            : ValidationResult.Failure("vector_provider_not_allowed_for_profile");
    }
}
