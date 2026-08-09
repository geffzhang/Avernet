using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.Auth;

internal sealed class FakeSecretResolver : ISecretResolver
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public static string LastLogOutput { get; private set; } = string.Empty;

    public FakeSecretResolver WithSecret(string key, string value)
    {
        _secrets[key] = value;
        return this;
    }

    public ValueTask<string> ResolveAsync(string secretKey, CancellationToken cancellationToken)
    {
        if (_secrets.TryGetValue(secretKey, out var value))
        {
            LastLogOutput = $"resolved secret: {keyOnly(secretKey)}";
            return ValueTask.FromResult(value);
        }

        throw new InvalidOperationException($"Secret '{keyOnly(secretKey)}' not found.");
    }

    private static string keyOnly(string s) => s;
}
