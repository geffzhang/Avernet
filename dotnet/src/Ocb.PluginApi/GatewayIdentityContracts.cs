using Ocb.Contracts;

namespace Ocb.PluginApi;

public interface IPrincipalTokenVerifier : IPluginContract
{
    ValueTask<CallerContext> VerifyAsync(
        string bearerToken,
        string signedPrincipalHeader,
        CancellationToken cancellationToken);
}
