using System.Text.Json.Serialization;

namespace Ocb.Runtime.Worker.Infra.Clients.Models;

/// <summary>
/// DTOs that mirror the raw JSON shapes returned by the engine upstream.
/// These are deserialized and then mapped to <see cref="Ocb.Contracts.Sessions"/>
/// types via <see cref="ResultMapping"/>.
/// </summary>

internal sealed record EngineSessionListResponse(
    [property: JsonPropertyName("sessions")]
    List<EngineSessionItem> Sessions);

internal sealed record EngineSessionItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("last_active_at")] string? LastActiveAt);

internal sealed record EngineSessionDetailResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("last_active_at")] string? LastActiveAt,
    [property: JsonPropertyName("messages")] List<EngineSessionMessage>? Messages);

internal sealed record EngineSessionMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("id")] string? Id);
