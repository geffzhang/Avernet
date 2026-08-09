using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.Auth;

internal sealed class FakeAccessKeyResolver : IAccessKeyResolver
{
    private readonly Dictionary<string, AccessKeyResult> _keys = new(StringComparer.Ordinal);

    public FakeAccessKeyResolver WithKey(string accessKeyToken, AccessKeyResult result)
    {
        _keys[accessKeyToken] = result;
        return this;
    }

    public ValueTask<AccessKeyResult?> ResolveAsync(string accessKeyToken, CancellationToken cancellationToken)
    {
        if (_keys.TryGetValue(accessKeyToken, out var result))
        {
            return ValueTask.FromResult<AccessKeyResult?>(result);
        }

        return ValueTask.FromResult<AccessKeyResult?>(null);
    }
}
