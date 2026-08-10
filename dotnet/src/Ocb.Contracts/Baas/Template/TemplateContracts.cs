namespace Ocb.Contracts.Baas.Template;

/// <summary>
/// Template service contract for managing bot device templates.
/// </summary>
public interface ITemplateServiceContract
{
    /// <summary>
    /// Gets a template by its UUID. Tenant-scoped.
    /// </summary>
    Task<TemplateDto> GetTemplateAsync(CallerContext caller, string templateUuid, CancellationToken ct = default);

    /// <summary>
    /// Lists templates for the caller's tenant.
    /// </summary>
    Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(CallerContext caller, CancellationToken ct = default);

    /// <summary>
    /// Creates a new template.
    /// </summary>
    Task<TemplateDto> CreateTemplateAsync(CallerContext caller, CreateTemplateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing template.
    /// </summary>
    Task<TemplateDto> UpdateTemplateAsync(CallerContext caller, string templateUuid, UpdateTemplateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a template.
    /// </summary>
    Task DeleteTemplateAsync(CallerContext caller, string templateUuid, CancellationToken ct = default);
}

public sealed record TemplateDto(
    string Uuid,
    string TenantId,
    string Name,
    string SandboxProfile,
    string ImageRef,
    int DefaultPort,
    int DefaultTtlMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTemplateRequest(
    string Name,
    string SandboxProfile,
    string ImageRef,
    int DefaultPort,
    int DefaultTtlMinutes);

public sealed record UpdateTemplateRequest(
    string? Name,
    string? SandboxProfile,
    string? ImageRef,
    int? DefaultPort,
    int? DefaultTtlMinutes);
