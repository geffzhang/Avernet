using Ocb.Contracts;

namespace Ocb.Gateway.Auth;

public sealed class TenantConsistencyFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var caller = (CallerContext?)context.HttpContext.Items[typeof(CallerContext)];
        if (caller is null)
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        var routeTenant = context.HttpContext.Request.RouteValues.TryGetValue("tenant_id", out var value)
            ? value?.ToString()
            : null;

        if (routeTenant is not null
            && !string.Equals(routeTenant, caller.TenantId, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<object?>(Results.Forbid());
        }

        return next(context);
    }
}
