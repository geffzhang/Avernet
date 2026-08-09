namespace Ocb.Contracts.Sessions;

/// <summary>
/// Session DTOs matching the engine OpenAPI parity-corpus contract.
/// No Orleans dependency — these are wire-level DTOs.
/// </summary>

public sealed record SessionListQuery(
    string? TenantId = null,
    string? Status = null,
    int? Limit = null,
    int? Offset = null);

public sealed record SessionSummaryDto(
    string SessionId,
    string Status,
    string? Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActiveAt);

public sealed record SessionDetailDto(
    string SessionId,
    string Status,
    string? Title,
    string? Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActiveAt,
    IReadOnlyList<SessionMessageDto>? RecentMessages);

public sealed record SessionMessageDto(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? MessageId);

public sealed record SessionUpdateRequest(
    string? Title = null,
    string? Status = null);

public sealed record ResetSessionResultDto(
    string SessionId,
    bool Reset,
    string? Message);
