using Ocb.Contracts;
using Ocb.PluginApi;

namespace Ocb.Gateway.Tests.Auth;

internal sealed class FakePrincipalTokenVerifier : IPrincipalTokenVerifier
{
    private readonly string _tenantId;

    public FakePrincipalTokenVerifier(string tenantId = "tenant-test")
    {
        _tenantId = tenantId;
    }

    public ValueTask<CallerContext> VerifyAsync(
        string bearerToken,
        string signedPrincipalHeader,
        CancellationToken cancellationToken)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal) { "user" };
        return ValueTask.FromResult(new CallerContext(_tenantId, "u1", roles));
    }
}
