namespace Ocb.PluginApi;

public interface IAccessKeyResolver : IPluginContract
{
    ValueTask<AccessKeyResult?> ResolveAsync(
        string accessKeyToken,
        CancellationToken cancellationToken);
}

public sealed record AccessKeyResult(
    string TenantId,
    string SubjectId,
    IReadOnlySet<string> Roles
);

public interface ISecretResolver : IPluginContract
{
    ValueTask<string> ResolveAsync(
        string secretKey,
        CancellationToken cancellationToken);
}
