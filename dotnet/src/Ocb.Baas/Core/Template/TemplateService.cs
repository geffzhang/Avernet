using Ocb.Baas.Core.Common;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Template;
using Ocb.PluginApi.Baas.Persistence;

namespace Ocb.Baas.Core.Template;

/// <summary>
/// Template service — manages bot device templates with tenant isolation.
/// </summary>
public sealed class TemplateService : ITemplateServiceContract
{
    private readonly ITemplateRepository _repository;

    public TemplateService(ITemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<TemplateDto> GetTemplateAsync(CallerContext caller, string templateUuid, CancellationToken ct = default)
    {
        var entity = await _repository.GetByTenantAndUuidAsync(caller.TenantId, templateUuid, ct);
        if (entity is null)
            throw new ResourceNotFoundException("TEMPLATE_NOT_FOUND");

        return MapToDto(entity);
    }

    public async Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(CallerContext caller, CancellationToken ct = default)
    {
        var entities = await _repository.ListByTenantAsync(caller.TenantId, ct);
        return entities.Select(MapToDto).ToList();
    }

    public async Task<TemplateDto> CreateTemplateAsync(CallerContext caller, CreateTemplateRequest request, CancellationToken ct = default)
    {
        var uuid = Guid.NewGuid().ToString("N")[..16];
        var now = DateTimeOffset.UtcNow;

        var record = new TemplateRecord(
            Uuid: uuid,
            TenantId: caller.TenantId,
            Name: request.Name,
            SandboxProfile: request.SandboxProfile,
            ImageRef: request.ImageRef,
            DefaultPort: request.DefaultPort,
            DefaultTtlMinutes: request.DefaultTtlMinutes,
            CreatedAt: now,
            UpdatedAt: now);

        await _repository.InsertAsync(record, ct);
        return MapToDto(record);
    }

    public async Task<TemplateDto> UpdateTemplateAsync(CallerContext caller, string templateUuid, UpdateTemplateRequest request, CancellationToken ct = default)
    {
        var existing = await _repository.GetByTenantAndUuidAsync(caller.TenantId, templateUuid, ct);
        if (existing is null)
            throw new ResourceNotFoundException("TEMPLATE_NOT_FOUND");

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            SandboxProfile = request.SandboxProfile ?? existing.SandboxProfile,
            ImageRef = request.ImageRef ?? existing.ImageRef,
            DefaultPort = request.DefaultPort ?? existing.DefaultPort,
            DefaultTtlMinutes = request.DefaultTtlMinutes ?? existing.DefaultTtlMinutes,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, ct);
        return MapToDto(updated);
    }

    public async Task DeleteTemplateAsync(CallerContext caller, string templateUuid, CancellationToken ct = default)
    {
        var existing = await _repository.GetByTenantAndUuidAsync(caller.TenantId, templateUuid, ct);
        if (existing is null)
            throw new ResourceNotFoundException("TEMPLATE_NOT_FOUND");

        await _repository.DeleteAsync(caller.TenantId, templateUuid, ct);
    }

    private static TemplateDto MapToDto(TemplateRecord r) => new(
        Uuid: r.Uuid,
        TenantId: r.TenantId,
        Name: r.Name,
        SandboxProfile: r.SandboxProfile,
        ImageRef: r.ImageRef,
        DefaultPort: r.DefaultPort,
        DefaultTtlMinutes: r.DefaultTtlMinutes,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}
