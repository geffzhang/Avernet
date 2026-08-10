using Ocb.Contracts;
using Ocb.Contracts.Identity;
using Ocb.PluginApi.Identity;

namespace Ocb.Backend.Identity;

/// <summary>
/// Resolves a caller identity binding for a given tenant, bot, and subject.
/// Validates tenant isolation — the resolved binding must match the caller's tenant.
/// </summary>
public sealed class CallerIdentityService
{
    private readonly ICallerIdentityRepositoryPlugin _repo;

    public CallerIdentityService(ICallerIdentityRepositoryPlugin repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Resolve the caller identity and verify tenant consistency.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when no binding exists or the tenant does not match.
    /// </exception>
    public async Task<CallerIdentityBinding> ResolveCallerAsync(
        CallerContext context, string botId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);

        var binding = await _repo.GetCallerIdentityAsync(
            context.TenantId, botId, context.SubjectId, ct);

        if (binding is null)
        {
            throw new UnauthorizedAccessException(
                $"Caller identity not found for tenant '{context.TenantId}', " +
                $"bot '{botId}', subject '{context.SubjectId}'.");
        }

        if (!string.Equals(binding.TenantId, context.TenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Tenant mismatch: the resolved binding belongs to a different tenant.");
        }

        return binding;
    }
}
