using System.Text.Json;
using Ocb.Contracts.Fusion;

namespace Ocb.Grains.Fusion;

// ── FusionRequestDto surrogates ──────────────────────────────────

[GenerateSerializer]
internal struct FuseOptionsDtoSurrogate
{
    [Id(0)] public int MaxParticipants;
    [Id(1)] public int TimeoutMs;
    [Id(2)] public bool RequireConsensus;
    [Id(3)] public float MinConfidence;
}

[GenerateSerializer]
internal struct FuseMetadataDtoSurrogate
{
    [Id(0)] public string? Source;
    [Id(1)] public List<string>? Tags;
    [Id(2)] public byte[]? CustomJson; // JsonElement serialized as UTF-8 bytes
}

[GenerateSerializer]
internal struct FusionRequestDtoSurrogate
{
    [Id(0)] public string Question;
    [Id(1)] public List<string> Participants;
    [Id(2)] public string? DriverBotId;
    [Id(3)] public string Mode;
    [Id(4)] public string FusionMode;
    [Id(5)] public FuseOptionsDtoSurrogate? Options;
    [Id(6)] public FuseMetadataDtoSurrogate? Metadata;
    [Id(7)] public string? SessionId;
}

[RegisterConverter]
internal sealed class FusionRequestDtoConverter : IConverter<FusionRequestDto, FusionRequestDtoSurrogate>
{
    public FusionRequestDto ConvertFromSurrogate(in FusionRequestDtoSurrogate s)
    {
        return new FusionRequestDto(
            Question: s.Question,
            Participants: s.Participants,
            DriverBotId: s.DriverBotId,
            Mode: s.Mode,
            FusionMode: s.FusionMode,
            Options: s.Options is { } o
                ? new FuseOptionsDto(o.MaxParticipants, o.TimeoutMs, o.RequireConsensus, o.MinConfidence)
                : null,
            Metadata: s.Metadata is { } m
                ? new FuseMetadataDto(m.Source, m.Tags,
                    m.CustomJson is { } b ? System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(b) : null)
                : null,
            SessionId: s.SessionId
        );
    }

    public FusionRequestDtoSurrogate ConvertToSurrogate(in FusionRequestDto value)
    {
        return new FusionRequestDtoSurrogate
        {
            Question = value.Question,
            Participants = value.Participants?.ToList() ?? [],
            DriverBotId = value.DriverBotId,
            Mode = value.Mode,
            FusionMode = value.FusionMode,
            Options = value.Options is { } o
                ? new FuseOptionsDtoSurrogate { MaxParticipants = o.MaxParticipants, TimeoutMs = o.TimeoutMs, RequireConsensus = o.RequireConsensus, MinConfidence = o.MinConfidence }
                : null,
            Metadata = value.Metadata is { } m
                ? new FuseMetadataDtoSurrogate
                {
                    Source = m.Source,
                    Tags = m.Tags?.ToList(),
                    CustomJson = m.Custom is { } je ? System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(je) : null
                }
                : null,
            SessionId = value.SessionId
        };
    }
}

// ── FuseResponseDto surrogates ───────────────────────────────────

[GenerateSerializer]
internal struct PerspectiveResponseDtoSurrogate
{
    [Id(0)] public string WorkerId;
    [Id(1)] public string ParticipantId;
    [Id(2)] public string Response;
    [Id(3)] public float Confidence;
    [Id(4)] public List<string>? Sources;
    [Id(5)] public long LatencyMs;
}

[GenerateSerializer]
internal struct RecommendationResponseDtoSurrogate
{
    [Id(0)] public string Content;
    [Id(1)] public int VoteCount;
    [Id(2)] public int TotalParticipants;
    [Id(3)] public float Confidence;
}

[GenerateSerializer]
internal struct TimingResponseDtoSurrogate
{
    [Id(0)] public long TotalMs;
    [Id(1)] public long EmbeddingMs;
    [Id(2)] public long SearchMs;
    [Id(3)] public long RerankMs;
    [Id(4)] public long AggregationMs;
}

[GenerateSerializer]
internal struct FuseResponseDtoSurrogate
{
    [Id(0)] public string GroupId;
    [Id(1)] public string FusionId;
    [Id(2)] public string Question;
    [Id(3)] public string? DriverBotId;
    [Id(4)] public List<PerspectiveResponseDtoSurrogate> Perspectives;
    [Id(5)] public RecommendationResponseDtoSurrogate? Recommendation;
    [Id(6)] public bool PartialSuccess;
    [Id(7)] public List<string> Warnings;
    [Id(8)] public List<string> Errors;
    [Id(9)] public TimingResponseDtoSurrogate Timing;
    [Id(10)] public string FusionMode;
}

[RegisterConverter]
internal sealed class FuseResponseDtoConverter : IConverter<FuseResponseDto, FuseResponseDtoSurrogate>
{
    public FuseResponseDto ConvertFromSurrogate(in FuseResponseDtoSurrogate s)
    {
        return new FuseResponseDto(
            GroupId: s.GroupId,
            FusionId: s.FusionId,
            Question: s.Question,
            DriverBotId: s.DriverBotId,
            Perspectives: s.Perspectives.ConvertAll(p => new PerspectiveResponseDto(
                p.WorkerId, p.ParticipantId, p.Response, p.Confidence, p.Sources, p.LatencyMs)),
            Recommendation: s.Recommendation is { } r
                ? new RecommendationResponseDto(r.Content, r.VoteCount, r.TotalParticipants, r.Confidence)
                : null,
            PartialSuccess: s.PartialSuccess,
            Warnings: s.Warnings,
            Errors: s.Errors,
            Timing: new TimingResponseDto(s.Timing.TotalMs, s.Timing.EmbeddingMs,
                s.Timing.SearchMs, s.Timing.RerankMs, s.Timing.AggregationMs),
            FusionMode: s.FusionMode
        );
    }

    public FuseResponseDtoSurrogate ConvertToSurrogate(in FuseResponseDto value)
    {
        return new FuseResponseDtoSurrogate
        {
            GroupId = value.GroupId,
            FusionId = value.FusionId,
            Question = value.Question,
            DriverBotId = value.DriverBotId,
            Perspectives = value.Perspectives.Select(p => new PerspectiveResponseDtoSurrogate
            {
                WorkerId = p.WorkerId,
                ParticipantId = p.ParticipantId,
                Response = p.Response,
                Confidence = p.Confidence,
                Sources = p.Sources?.ToList(),
                LatencyMs = p.LatencyMs
            }).ToList(),
            Recommendation = value.Recommendation is { } r
                ? new RecommendationResponseDtoSurrogate
                {
                    Content = r.Content, VoteCount = r.VoteCount,
                    TotalParticipants = r.TotalParticipants, Confidence = r.Confidence
                }
                : null,
            PartialSuccess = value.PartialSuccess,
            Warnings = value.Warnings?.ToList() ?? [],
            Errors = value.Errors?.ToList() ?? [],
            Timing = new TimingResponseDtoSurrogate
            {
                TotalMs = value.Timing.TotalMs, EmbeddingMs = value.Timing.EmbeddingMs,
                SearchMs = value.Timing.SearchMs, RerankMs = value.Timing.RerankMs,
                AggregationMs = value.Timing.AggregationMs
            },
            FusionMode = value.FusionMode
        };
    }
}
