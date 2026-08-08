namespace Ocb.Configuration;

public sealed record OcbPlatformOptions(DeploymentProfile Profile, VectorProvider VectorProvider);

public sealed record ValidationResult(bool IsValid, string? ErrorCode)
{
    public static ValidationResult Success { get; } = new(true, null);

    public static ValidationResult Failure(string code) => new(false, code);
}
