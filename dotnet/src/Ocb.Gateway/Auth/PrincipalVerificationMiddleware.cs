using System.Net;
using Ocb.Contracts;
using Ocb.PluginApi;

namespace Ocb.Gateway.Auth;

public sealed class PrincipalVerificationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var verifier = context.RequestServices.GetService<IPrincipalTokenVerifier>();
        if (verifier is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            return;
        }

        var jwt = context.Request.Headers.Authorization.ToString();
        var signedPrincipal = context.Request.Headers["X-Avernet-Principal"].ToString();

        if (string.IsNullOrWhiteSpace(jwt) || string.IsNullOrWhiteSpace(signedPrincipal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        var caller = await verifier.VerifyAsync(jwt, signedPrincipal, context.RequestAborted);

        var accessKeyResolver = context.RequestServices.GetService<IAccessKeyResolver>();
        if (accessKeyResolver is not null)
        {
            var accessKeyToken = context.Request.Headers["X-Avernet-Access-Key"].ToString();
            if (!string.IsNullOrWhiteSpace(accessKeyToken))
            {
                var resolved = await accessKeyResolver.ResolveAsync(accessKeyToken, context.RequestAborted);
                if (resolved is null
                    || !string.Equals(resolved.TenantId, caller.TenantId, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }
            }
        }

        context.Items[typeof(CallerContext)] = caller;
        await next(context);
    }
}
