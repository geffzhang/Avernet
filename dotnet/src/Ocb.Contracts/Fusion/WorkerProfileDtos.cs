using System.Text.Json.Serialization;

namespace Ocb.Contracts.Fusion;

/// <summary>
/// Request to create a new worker.
/// </summary>
public sealed record CreateWorkerRequest(
    [property: JsonPropertyName("worker_id")] string WorkerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("config")] WorkerConfigurationDto Config
);

/// <summary>
/// Worker configuration details.
/// </summary>
public sealed record WorkerConfigurationDto(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("temperature")] float Temperature,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system_prompt")] string? SystemPrompt
);

/// <summary>
/// Worker profile returned in list and detail responses.
/// </summary>
public sealed record WorkerProfileDto(
    [property: JsonPropertyName("worker_id")] string WorkerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("config")] WorkerConfigurationDto Config,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt
);

/// <summary>
/// Paginated list of workers.
/// </summary>
public sealed record WorkerListResponse(
    [property: JsonPropertyName("workers")] IReadOnlyList<WorkerProfileDto> Workers,
    [property: JsonPropertyName("total")] int Total
);

/// <summary>
/// Patch body for updating a worker.
/// </summary>
public sealed record UpdateWorkerRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("config")] WorkerConfigurationDto? Config,
    [property: JsonPropertyName("status")] string? Status
);

/// <summary>
/// Worker detail result.
/// </summary>
public sealed record WorkerDetailResponse(
    [property: JsonPropertyName("worker")] WorkerProfileDto Worker
);
