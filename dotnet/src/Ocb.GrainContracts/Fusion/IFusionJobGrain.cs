namespace Ocb.GrainContracts.Fusion;

/// <summary>
/// Fusion job grain — serial coordination for a single fusion operation.
/// Each grain instance is keyed <c>fusion-job/{tenantId}/{fusionJobId}</c>
/// and ensures idempotent execution via <see cref="FusionCommand.IdempotencyKey"/>.
/// </summary>
/// <remarks>
/// The grain delegates actual fusion work to <see cref="IFusionCoordinator"/>
/// and never performs HTTP calls, vector I/O, or database CRUD directly.
/// </remarks>
[Alias("Ocb.GrainContracts.Fusion.IFusionJobGrain")]
public interface IFusionJobGrain : IGrainWithStringKey
{
    /// <summary>
    /// Execute a fusion command. Repeated calls with the same
    /// <see cref="FusionCommand.IdempotencyKey"/> return the cached result
    /// without re-execution.
    /// </summary>
    Task<Ocb.Contracts.Fusion.FuseResponseDto> ExecuteAsync(FusionCommand command);

    /// <summary>
    /// Return the current persistent state of this grain.
    /// </summary>
    Task<FusionJobState> GetStateAsync();
}
