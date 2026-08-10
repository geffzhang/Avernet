using Ocb.Contracts.Fusion;
using Ocb.GrainContracts.Fusion;

namespace Ocb.Fusion.Application;

/// <summary>
/// Domain orchestrator for fusion operations: embedding → vector search →
/// reranker → aggregation. Injected into <see cref="IFusionJobGrain"/> grains
/// so grains stay thin coordinators that never perform I/O directly.
/// </summary>
/// <remarks>
/// Stub implementation for route parity. The full pipeline will be wired
/// when <see cref="Ocb.PluginApi.Contracts.Vector.IEmbeddingPlugin"/>,
/// <see cref="Ocb.PluginApi.Contracts.Vector.IHybridSearchStore"/>, and
/// <see cref="Ocb.PluginApi.Contracts.Vector.IRerankerPlugin"/> are available.
/// </remarks>
public sealed class FusionCoordinator : IFusionCoordinator
{
    /// <inheritdoc />
    public Task<FuseResponseDto> RunAsync(FusionCommand command, CancellationToken ct)
    {
        var participants = command.Request.Participants ?? Array.Empty<string>();
        var fusionId = $"fuse-{Guid.NewGuid():N}";

        var response = new FuseResponseDto(
            GroupId: "default",
            FusionId: fusionId,
            Question: command.Request.Question ?? string.Empty,
            DriverBotId: command.Request.DriverBotId,
            Perspectives: participants
                .Select(p => new PerspectiveResponseDto(
                    WorkerId: p,
                    ParticipantId: p,
                    Response: $"Perspective from {p}",
                    Confidence: 0.85f,
                    Sources: null,
                    LatencyMs: 50))
                .ToList(),
            Recommendation: new RecommendationResponseDto(
                Content: "Synthesized recommendation based on all perspectives.",
                VoteCount: participants.Count,
                TotalParticipants: participants.Count,
                Confidence: 0.9f),
            PartialSuccess: false,
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            Timing: new TimingResponseDto(100, 20, 50, 10, 20),
            FusionMode: command.Request.FusionMode ?? "agent"
        );

        return Task.FromResult(response);
    }
}
