using Microsoft.AspNetCore.Http;
using Ocb.Contracts;

namespace Ocb.Fusion.Application;

/// <summary>
/// Factory to extract CallerContext from HTTP claims/headers.
/// </summary>
public static class CallerContextFactory
{
    private const string TenantHeaderName = "X-Tenant-Id";
    private const string SubjectHeaderName = "X-Subject-Id";
    private const string RolesHeaderName = "X-Roles";

    /// <summary>
    /// Extract CallerContext from HttpContext headers.
    /// Falls back to defaults when headers are absent (for development/testing).
    /// </summary>
    public static CallerContext FromHttp(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        var tenantId = http.Request.Headers[TenantHeaderName].FirstOrDefault()
            ?? http.User.FindFirst("tenant_id")?.Value
            ?? "default";

        var subjectId = http.Request.Headers[SubjectHeaderName].FirstOrDefault()
            ?? http.User.FindFirst("sub")?.Value
            ?? "anonymous";

        var rolesHeader = http.Request.Headers[RolesHeaderName].FirstOrDefault();
        var roles = rolesHeader is not null
            ? new HashSet<string>(rolesHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal) { "user" };

        return new CallerContext(tenantId, subjectId, roles);
    }
}
