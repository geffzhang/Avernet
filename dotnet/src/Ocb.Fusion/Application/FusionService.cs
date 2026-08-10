using Ocb.Contracts;
using Ocb.Contracts.Fusion;

namespace Ocb.Fusion.Application;

/// <summary>
/// Domain service for fusion orchestration.
/// Coordinates embedding → vector search → reranking → aggregation.
/// </summary>
/// <remarks>
/// Will receive DI dependencies (IEmbeddingPlugin, IHybridSearchStore, IRerankerPlugin) when
/// the full pipeline is wired up. Currently stubbed for route parity testing.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Will be injected with DI dependencies in the full pipeline implementation.")]
public sealed class FusionService
{
    /// <summary>
    /// Execute a fusion request for a given group.
    /// </summary>
    public Task<FuseResponseDto> FuseAsync(
        string groupId,
        FusionRequestDto request,
        CallerContext caller,
        CancellationToken ct)
    {
        var fusionId = $"fuse-{Guid.NewGuid():N}";

        var participants = request.Participants ?? Array.Empty<string>();
        var question = request.Question ?? string.Empty;
        var fusionMode = request.FusionMode ?? "agent";

        var response = new FuseResponseDto(
            GroupId: groupId,
            FusionId: fusionId,
            Question: question,
            DriverBotId: request.DriverBotId,
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
            FusionMode: fusionMode
        );

        return Task.FromResult(response);
    }
}
