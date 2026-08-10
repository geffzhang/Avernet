using Ocb.GrainContracts.Channels;
using Ocb.GrainContracts.Fusion;

namespace Ocb.Grains.Channels;

/// <summary>
/// Orleans incoming call filter that validates tenant-key consistency
/// for tenant-scoped grain calls.
///
/// Supports <see cref="IConnectionDirectoryGrain"/> and
/// <see cref="IFusionJobGrain"/>; extend with additional grain types
/// as needed.
/// </summary>
public sealed class TenantKeyGuardCallFilter : IIncomingGrainCallFilter
{
    /// <inheritdoc />
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // Connection directory guard: key "directory/{tenantId}" must match RequestContext
        if (context.Grain is IConnectionDirectoryGrain
            && (context.Grain as IAddressable)?.GetPrimaryKeyString() is { } directoryKey
            && RequestContext.Get("TenantId") is string callerTenant
            && !directoryKey.EndsWith($"/{callerTenant}", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Tenant mismatch: caller={callerTenant} grain_key={directoryKey}");
        }

        // Fusion job guard: key "fusion-job/{tenantId}/{fusionJobId}" must match
        // the tenant stored in RequestContext under "TenantId"
        if (context.Grain is IFusionJobGrain
            && (context.Grain as IAddressable)?.GetPrimaryKeyString() is { } fusionJobKey
            && RequestContext.Get("TenantId") is string fusionCallerTenant)
        {
            var keyParts = fusionJobKey.Split('/');
            if (keyParts.Length >= 2
                && !string.Equals(fusionCallerTenant, keyParts[1], StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Tenant mismatch in FusionJobGrain: " +
                    $"caller={fusionCallerTenant} grain={keyParts[1]}");
            }
        }

        await context.Invoke();
    }
}
