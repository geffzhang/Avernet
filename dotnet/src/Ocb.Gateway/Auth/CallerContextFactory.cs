using Ocb.Contracts;

namespace Ocb.Gateway.Auth;

public static class CallerContextFactory
{
    public static CallerContext Create(string tenantId, string subjectId, IReadOnlySet<string> roles)
    {
        return new CallerContext(tenantId, subjectId, roles);
    }
}
