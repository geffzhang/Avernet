using Ocb.Contracts.Fusion;

namespace Ocb.GrainContracts.Fusion;

/// <summary>
/// Domain orchestrator for fusion operations: embedding → vector search →
/// reranker → aggregation. Injected into <see cref="IFusionJobGrain"/> grains
/// so the grain stays a thin coordinator that never performs I/O directly.
/// </summary>
/// <remarks>
/// While the implementation lives in <c>Ocb.Fusion.Application</c>, this
/// interface is defined here in the contracts layer so <c>Ocb.Grains</c>
/// can consume it without referencing the HTTP delivery adapter.
/// </remarks>
public interface IFusionCoordinator
{
    /// <summary>
    /// Run the full fusion pipeline and return a synthesized response.
    /// </summary>
    Task<FuseResponseDto> RunAsync(FusionCommand command, CancellationToken ct);
}
