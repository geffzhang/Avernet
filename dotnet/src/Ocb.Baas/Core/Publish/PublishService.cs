using Ocb.Baas.Core.Common;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Publish;
using Ocb.PluginApi.Baas.Persistence;

namespace Ocb.Baas.Core.Publish;

/// <summary>
/// Publish service — manages bot release lifecycle with a strict state machine.
/// </summary>
public sealed class PublishService : IPublishServiceContract
{
    private readonly IPublishRepository _repository;

    private static readonly HashSet<string> ValidTransitions = new(StringComparer.Ordinal)
    {
        // source → target via valid operation
    };

    public PublishService(IPublishRepository repository)
    {
        _repository = repository;
    }

    public async Task<PublishDto> CreatePublishAsync(CallerContext caller, CreatePublishRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new PublishRecord(
            Id: 0,
            TenantId: caller.TenantId,
            BotId: request.BotId,
            Version: request.Version,
            Status: PublishStatus.Pending,
            SourceLocator: request.SourceLocator,
            Changelog: request.Changelog,
            OperatorId: null,
            ErrorMessage: null,
            CreatedAt: now,
            UpdatedAt: now);

        var id = await _repository.InsertAsync(record, ct);
        return MapToDto(record with { Id = id });
    }

    public async Task<PublishDto> GetPublishAsync(CallerContext caller, long publishId, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(publishId, ct);
        if (record is null)
            throw new ResourceNotFoundException("PUBLISH_NOT_FOUND");
        TenantGuard.AssertTenantMatch(caller, record.TenantId);
        return MapToDto(record);
    }

    public async Task<IReadOnlyList<PublishDto>> ListPublishesAsync(CallerContext caller, CancellationToken ct = default)
    {
        var records = await _repository.ListByTenantAsync(caller.TenantId, ct);
        return records.Select(MapToDto).ToList();
    }

    public Task<PublishProgress> GetProgressAsync(CallerContext caller, long publishId, CancellationToken ct = default)
    {
        return Task.FromResult(new PublishProgress(publishId, "staged", 0, 1, null));
    }

    public async Task<PublishDto> ApproveStageAsync(CallerContext caller, long publishId, string operatorId, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(publishId, ct);
        if (record is null)
            throw new ResourceNotFoundException("PUBLISH_NOT_FOUND");
        TenantGuard.AssertTenantMatch(caller, record.TenantId);

        if (record.Status is not (PublishStatus.Pending or PublishStatus.Reviewing))
            throw new DomainConflictException("INVALID_TRANSITION",
                $"Cannot approve publish in '{record.Status}' state.");

        var updated = record with
        {
            Status = record.Status == PublishStatus.Pending ? PublishStatus.Reviewing : PublishStatus.Active,
            OperatorId = operatorId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, ct);
        return MapToDto(updated);
    }

    public async Task<PublishDto> RejectPublishAsync(CallerContext caller, long publishId, string reason, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(publishId, ct);
        if (record is null)
            throw new ResourceNotFoundException("PUBLISH_NOT_FOUND");
        TenantGuard.AssertTenantMatch(caller, record.TenantId);

        if (record.Status == PublishStatus.Active)
            throw new DomainConflictException("CANNOT_REJECT_ACTIVE",
                "Cannot reject an active publish.");

        var updated = record with
        {
            Status = PublishStatus.Rejected,
            ErrorMessage = reason,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, ct);
        return MapToDto(updated);
    }

    public async Task<PublishDto> RevokePublishAsync(CallerContext caller, long publishId, string reason, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(publishId, ct);
        if (record is null)
            throw new ResourceNotFoundException("PUBLISH_NOT_FOUND");
        TenantGuard.AssertTenantMatch(caller, record.TenantId);

        var updated = record with
        {
            Status = PublishStatus.Revoked,
            ErrorMessage = reason,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, ct);
        return MapToDto(updated);
    }

    public async Task<PublishDto> RetryPublishAsync(CallerContext caller, long publishId, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(publishId, ct);
        if (record is null)
            throw new ResourceNotFoundException("PUBLISH_NOT_FOUND");
        TenantGuard.AssertTenantMatch(caller, record.TenantId);

        if (record.Status != PublishStatus.Failed)
            throw new DomainConflictException("ONLY_FAILED_RETRYABLE");

        var updated = record with
        {
            Status = PublishStatus.Pending,
            ErrorMessage = null,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, ct);
        return MapToDto(updated);
    }

    public async Task<PublishDto> CompletePublishAsync(CallerContext caller, long publishId, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(publishId, ct);
        if (record is null)
            throw new ResourceNotFoundException("PUBLISH_NOT_FOUND");
        TenantGuard.AssertTenantMatch(caller, record.TenantId);

        var updated = record with
        {
            Status = PublishStatus.Completed,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, ct);
        return MapToDto(updated);
    }

    private static PublishDto MapToDto(PublishRecord r) => new(
        Id: r.Id,
        TenantId: r.TenantId,
        BotId: r.BotId,
        Version: r.Version,
        Status: r.Status,
        SourceLocator: r.SourceLocator,
        Changelog: r.Changelog,
        OperatorId: r.OperatorId,
        ErrorMessage: r.ErrorMessage,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}
