using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocb.Contracts.Fusion;

/// <summary>
/// Request DTO for the fuse endpoint — aligned with bcsfuse FusionRequest schema.
/// </summary>
public sealed record FusionRequestDto(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("participants")] IReadOnlyList<string> Participants,
    [property: JsonPropertyName("driver_bot_id")] string? DriverBotId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("fusion_mode")] string FusionMode,
    [property: JsonPropertyName("options")] FuseOptionsDto? Options,
    [property: JsonPropertyName("metadata")] FuseMetadataDto? Metadata,
    [property: JsonPropertyName("session_id")] string? SessionId
);

/// <summary>
/// Fuse options controlling participant engagement and result preferences.
/// </summary>
public sealed record FuseOptionsDto(
    [property: JsonPropertyName("max_participants")] int MaxParticipants,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs,
    [property: JsonPropertyName("require_consensus")] bool RequireConsensus,
    [property: JsonPropertyName("min_confidence")] float MinConfidence
)
{
    public static readonly FuseOptionsDto Default = new(
        MaxParticipants: 5,
        TimeoutMs: 30_000,
        RequireConsensus: false,
        MinConfidence: 0.0f
    );
}

/// <summary>
/// Optional metadata bag carried through the fusion pipeline.
/// </summary>
public sealed record FuseMetadataDto(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags,
    [property: JsonPropertyName("custom")] JsonElement? Custom
);

/// <summary>
/// Fusion result returned to the caller.
/// </summary>
public sealed record FuseResponseDto(
    [property: JsonPropertyName("group_id")] string GroupId,
    [property: JsonPropertyName("fusion_id")] string FusionId,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("driver_bot_id")] string? DriverBotId,
    [property: JsonPropertyName("perspectives")] IReadOnlyList<PerspectiveResponseDto> Perspectives,
    [property: JsonPropertyName("recommendation")] RecommendationResponseDto? Recommendation,
    [property: JsonPropertyName("partial_success")] bool PartialSuccess,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("timing")] TimingResponseDto Timing,
    [property: JsonPropertyName("fusion_mode")] string FusionMode
);

/// <summary>
/// Single participant's perspective within a fusion result.
/// </summary>
public sealed record PerspectiveResponseDto(
    [property: JsonPropertyName("worker_id")] string WorkerId,
    [property: JsonPropertyName("participant_id")] string ParticipantId,
    [property: JsonPropertyName("response")] string Response,
    [property: JsonPropertyName("confidence")] float Confidence,
    [property: JsonPropertyName("sources")] IReadOnlyList<string>? Sources,
    [property: JsonPropertyName("latency_ms")] long LatencyMs
);

/// <summary>
/// Synthesized recommendation from the fusion driver bot.
/// </summary>
public sealed record RecommendationResponseDto(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("vote_count")] int VoteCount,
    [property: JsonPropertyName("total_participants")] int TotalParticipants,
    [property: JsonPropertyName("confidence")] float Confidence
);

/// <summary>
/// Timing breakdown for the fusion operation.
/// </summary>
public sealed record TimingResponseDto(
    [property: JsonPropertyName("total_ms")] long TotalMs,
    [property: JsonPropertyName("embedding_ms")] long EmbeddingMs,
    [property: JsonPropertyName("search_ms")] long SearchMs,
    [property: JsonPropertyName("rerank_ms")] long RerankMs,
    [property: JsonPropertyName("aggregation_ms")] long AggregationMs
);
