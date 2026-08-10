using Ocb.Contracts.Fusion;
using Ocb.GrainContracts.Fusion;

namespace Ocb.Grains.Fusion;

/// <summary>
/// Thin coordination grain for a single fusion operation.
/// Ensures idempotent execution via <see cref="FusionJobState.CompletedByIdempotency"/>
/// and delegates actual fusion work to <see cref="IFusionCoordinator"/>.
///
/// Never performs HTTP calls, vector I/O, database CRUD, or file I/O directly.
/// </summary>
public sealed class FusionJobGrain : Grain, IFusionJobGrain
{
    private readonly IPersistentState<FusionJobState> _state;
    private readonly IFusionCoordinator _fusionCoordinator;

    public FusionJobGrain(
        [PersistentState("fusion-job", "orleans-storage")]
        IPersistentState<FusionJobState> state,
        IFusionCoordinator fusionCoordinator)
    {
        _state = state;
        _fusionCoordinator = fusionCoordinator;
    }

    /// <inheritdoc />
    public async Task<FuseResponseDto> ExecuteAsync(FusionCommand command)
    {
        // Primary tenant guard: validate caller tenant matches grain key tenant
        var keyParts = this.GetPrimaryKeyString().Split('/');
        if (keyParts.Length >= 2
            && !string.Equals(command.Caller.TenantId, keyParts[1], StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Tenant mismatch in FusionJobGrain: " +
                $"caller={command.Caller.TenantId} grain={keyParts[1]}");
        }

        // Idempotency: return cached result for previously completed keys
        if (_state.State.CompletedByIdempotency
            .TryGetValue(command.IdempotencyKey, out var cached))
        {
            return cached;
        }

        // Delegate to domain coordinator (Grain never performs I/O directly)
        var result = await _fusionCoordinator.RunAsync(command, CancellationToken.None);

        // Persist idempotency record
        _state.State.CompletedByIdempotency[command.IdempotencyKey] = result;
        _state.State.ExecutionCount++;
        _state.State.LastFusionId = result.FusionId;
        await _state.WriteStateAsync();

        return result;
    }

    /// <inheritdoc />
    public Task<FusionJobState> GetStateAsync() =>
        Task.FromResult(_state.State);
}
