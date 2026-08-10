namespace Ocb.PluginApi.Baas.Persistence;

/// <summary>
/// Persistence ports for BaaS domain services. These are consumed by
/// BaaS Core services and implemented by the PostgreSQL infrastructure layer.
/// </summary>
public interface ITenantRepository
{
    Task<TenantRecord?> GetByTenantIdAsync(string tenantId, CancellationToken ct = default);
    Task UpsertAsync(TenantRecord record, CancellationToken ct = default);
}

public interface ITemplateRepository
{
    Task<TemplateRecord?> GetByTenantAndUuidAsync(string tenantId, string templateUuid, CancellationToken ct = default);
    Task<IReadOnlyList<TemplateRecord>> ListByTenantAsync(string tenantId, CancellationToken ct = default);
    Task InsertAsync(TemplateRecord record, CancellationToken ct = default);
    Task UpdateAsync(TemplateRecord record, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string templateUuid, CancellationToken ct = default);
}

public interface IDeviceRepository
{
    Task<DeviceRecord?> GetByIdAsync(string deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceRecord>> ListByBotAsync(string tenantId, string botId, CancellationToken ct = default);
    Task InsertAsync(DeviceRecord record, CancellationToken ct = default);
    Task UpdateStatusAsync(string deviceId, string status, DateTimeOffset? expiresAt, CancellationToken ct = default);
    Task DeleteAsync(string deviceId, CancellationToken ct = default);
}

public interface IPublishRepository
{
    Task<PublishRecord?> GetByIdAsync(long publishId, CancellationToken ct = default);
    Task<IReadOnlyList<PublishRecord>> ListByTenantAsync(string tenantId, CancellationToken ct = default);
    Task<long> InsertAsync(PublishRecord record, CancellationToken ct = default);
    Task UpdateAsync(PublishRecord record, CancellationToken ct = default);
}

public interface IBotRunQueueRepository
{
    Task<long> EnqueueAsync(BotRunQueueRecord record, CancellationToken ct = default);
    Task<BotRunQueueRecord?> LeaseNextAsync(string workerId, CancellationToken ct = default);
    Task AckAsync(long entryId, string workerId, CancellationToken ct = default);
    Task DeadLetterAsync(long entryId, string reason, CancellationToken ct = default);
}

// Persistence record types (POCOs for repository ports)

public sealed record TenantRecord(
    string TenantId,
    string DisplayName,
    string Status,
    int MaxBots,
    int MaxDevices,
    long QuotaPerMinute,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TemplateRecord(
    string Uuid,
    string TenantId,
    string Name,
    string SandboxProfile,
    string ImageRef,
    int DefaultPort,
    int DefaultTtlMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DeviceRecord(
    string DeviceId,
    string TenantId,
    string BotId,
    string TemplateUuid,
    string SandboxProfile,
    string Status,
    string? SandboxId,
    string? InternalEndpoint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset UpdatedAt);

public sealed record PublishRecord(
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

public sealed record BotRunQueueRecord(
    long Id,
    string BotId,
    string RunType,
    string? Payload,
    int Priority,
    string Status,
    int Attempt,
    int MaxRetries,
    string? WorkerId,
    string? LeaseToken,
    DateTimeOffset? LeasedAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
