using Ocb.GrainContracts.Channels;

namespace Ocb.Grains.Channels;

/// <summary>
/// Orleans incoming call filter that validates tenant-key consistency
/// for <see cref="IConnectionDirectoryGrain"/> calls.
///
/// The grain key <c>directory/{tenantId}</c> must match the caller's
/// tenant identity stored in <see cref="RequestContext"/> under the
/// <c>TenantId</c> key.
/// </summary>
public sealed class TenantKeyGuardCallFilter : IIncomingGrainCallFilter
{
    /// <inheritdoc />
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is IConnectionDirectoryGrain
            && (context.Grain as IAddressable)?.GetPrimaryKeyString() is { } grainKey
            && RequestContext.Get("TenantId") is string callerTenant
            && !grainKey.EndsWith($"/{callerTenant}", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Tenant mismatch: caller={callerTenant} grain_key={grainKey}");
        }

        await context.Invoke();
    }
}
