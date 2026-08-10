namespace Ocb.Contracts.Baas.Publish;

/// <summary>
/// Publish service contract for bot release lifecycle management.
/// </summary>
public interface IPublishServiceContract
{
    /// <summary>
    /// Creates a new publish request.
    /// </summary>
    Task<PublishDto> CreatePublishAsync(CallerContext caller, CreatePublishRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets the current state of a publish.
    /// </summary>
    Task<PublishDto> GetPublishAsync(CallerContext caller, long publishId, CancellationToken ct = default);

    /// <summary>
    /// Lists publishes for the caller's tenant.
    /// </summary>
    Task<IReadOnlyList<PublishDto>> ListPublishesAsync(CallerContext caller, CancellationToken ct = default);

    /// <summary>
    /// Gets the progress of a running publish.
    /// </summary>
    Task<PublishProgress> GetProgressAsync(CallerContext caller, long publishId, CancellationToken ct = default);

    /// <summary>
    /// Approves a publish stage.
    /// </summary>
    Task<PublishDto> ApproveStageAsync(CallerContext caller, long publishId, string operatorId, CancellationToken ct = default);

    /// <summary>
    /// Rejects a publish.
    /// </summary>
    Task<PublishDto> RejectPublishAsync(CallerContext caller, long publishId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Revokes a published release.
    /// </summary>
    Task<PublishDto> RevokePublishAsync(CallerContext caller, long publishId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Retries a failed publish stage.
    /// </summary>
    Task<PublishDto> RetryPublishAsync(CallerContext caller, long publishId, CancellationToken ct = default);

    /// <summary>
    /// Marks a publish as complete.
    /// </summary>
    Task<PublishDto> CompletePublishAsync(CallerContext caller, long publishId, CancellationToken ct = default);
}

public sealed record CreatePublishRequest(
    string BotId,
    string Version,
    string SourceLocator,
    string? Changelog);

public sealed record PublishDto(
    long Id,
    string TenantId,
    string BotId,
    string Version,
    string Status,
    string SourceLocator,
    string? Changelog,
    string? OperatorId,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PublishProgress(
    long PublishId,
    string Stage,
    int CompletedSteps,
    int TotalSteps,
    string? Detail);

public static class PublishStatus
{
    public const string Pending = "pending";
    public const string Reviewing = "reviewing";
    public const string Active = "active";
    public const string Rejected = "rejected";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
    public const string Completed = "completed";
}
