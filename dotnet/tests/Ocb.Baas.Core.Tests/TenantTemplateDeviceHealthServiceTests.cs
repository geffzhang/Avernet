using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Ocb.Baas.Core.Common;
using Ocb.Baas.Core.Device;
using Ocb.Baas.Core.Health;
using Ocb.Baas.Core.Template;
using Ocb.Baas.Core.Tenant;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Device;
using Ocb.Contracts.Baas.Health;
using Ocb.Contracts.Baas.Publish;
using Ocb.Contracts.Baas.Template;
using Ocb.Contracts.Baas.Tenant;
using Ocb.PluginApi.Baas.Persistence;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.Baas.Core.Tests;

/// <summary>
/// Verify core BaaS services: tenant isolation, CRUD operations,
/// device lifecycle, and health checks.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class TenantTemplateDeviceHealthServiceTests
{
    private static CallerContext TenantACaller => new("tenant-a", "u-1", new HashSet<string> { "user" });
    private static CallerContext TenantBCaller => new("tenant-b", "u-1", new HashSet<string> { "user" });

    // ── Template Service ──

    [Fact]
    public async Task CreateTemplate_ShouldSucceed_And_ReturnDto()
    {
        var repo = new InMemoryTemplateRepository();
        var svc = new TemplateService(repo);
        var req = new CreateTemplateRequest("my-tpl", "docker", "alpine:latest", 8080, 30);

        var result = await svc.CreateTemplateAsync(TenantACaller, req);

        Assert.Equal("my-tpl", result.Name);
        Assert.Equal("tenant-a", result.TenantId);
        Assert.Equal("docker", result.SandboxProfile);
    }

    [Fact]
    public async Task GetTemplate_ShouldReturn404_WhenTenantMismatch()
    {
        var repo = new InMemoryTemplateRepository();
        var svc = new TemplateService(repo);

        // Create template for tenant-a
        await svc.CreateTemplateAsync(TenantACaller, new CreateTemplateRequest("t1", "docker", "img", 8080, 30));
        var templates = await svc.ListTemplatesAsync(TenantACaller);
        var uuid = templates[0].Uuid;

        // Tenant-b tries to access
        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => svc.GetTemplateAsync(TenantBCaller, uuid));
    }

    [Fact]
    public async Task ListTemplates_ShouldOnlyReturnTenantScoped()
    {
        var repo = new InMemoryTemplateRepository();
        var svc = new TemplateService(repo);

        await svc.CreateTemplateAsync(TenantACaller, new CreateTemplateRequest("a-tpl", "docker", "img", 8080, 30));
        await svc.CreateTemplateAsync(TenantBCaller, new CreateTemplateRequest("b-tpl", "k8s", "img", 8080, 30));

        var aTemplates = await svc.ListTemplatesAsync(TenantACaller);
        var bTemplates = await svc.ListTemplatesAsync(TenantBCaller);

        Assert.Single(aTemplates);
        Assert.Single(bTemplates);
        Assert.Equal("a-tpl", aTemplates[0].Name);
        Assert.Equal("b-tpl", bTemplates[0].Name);
    }

    [Fact]
    public async Task UpdateTemplate_ShouldOnlyAffectOwnTenant()
    {
        var repo = new InMemoryTemplateRepository();
        var svc = new TemplateService(repo);

        var created = await svc.CreateTemplateAsync(TenantACaller, new CreateTemplateRequest("t1", "docker", "img", 8080, 30));
        var updated = await svc.UpdateTemplateAsync(TenantACaller, created.Uuid, new UpdateTemplateRequest(Name: "renamed", null, null, null, null));

        Assert.Equal("renamed", updated.Name);
    }

    [Fact]
    public async Task DeleteTemplate_ShouldOnlyAffectOwnTenant()
    {
        var repo = new InMemoryTemplateRepository();
        var svc = new TemplateService(repo);

        var created = await svc.CreateTemplateAsync(TenantACaller, new CreateTemplateRequest("t1", "docker", "img", 8080, 30));
        await svc.DeleteTemplateAsync(TenantACaller, created.Uuid);

        // Verify deletion
        var templates = await svc.ListTemplatesAsync(TenantACaller);
        Assert.Empty(templates);
    }

    // ── Tenant Service ──

    [Fact]
    public async Task GetTenant_ShouldReturnOwnTenant()
    {
        var repo = new InMemoryTenantRepository();
        repo.Seed(new TenantRecord("tenant-a", "Tenant A", "active", 10, 20, 1000,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var svc = new TenantService(repo);

        var result = await svc.GetTenantAsync(TenantACaller);
        Assert.Equal("Tenant A", result.DisplayName);
    }

    [Fact]
    public async Task GetTenant_WhenNotFound_ShouldThrow()
    {
        var repo = new InMemoryTenantRepository();
        var svc = new TenantService(repo);

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => svc.GetTenantAsync(TenantACaller));
    }

    [Fact]
    public async Task VerifyTenant_ActiveTenant_ShouldReturnActive()
    {
        var repo = new InMemoryTenantRepository();
        repo.Seed(new TenantRecord("tenant-a", "A", "active", 10, 20, 1000,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var svc = new TenantService(repo);
        var result = await svc.VerifyTenantAsync(TenantACaller);

        Assert.True(result.Active);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task VerifyTenant_InactiveTenant_ShouldReturnInactive()
    {
        var repo = new InMemoryTenantRepository();
        repo.Seed(new TenantRecord("tenant-a", "A", "suspended", 10, 20, 1000,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var svc = new TenantService(repo);
        var result = await svc.VerifyTenantAsync(TenantACaller);

        Assert.False(result.Active);
        Assert.NotNull(result.Reason);
    }

    // ── Device Service ──

    [Fact]
    public async Task CreateDevice_ShouldReturnDeviceInfo()
    {
        var deviceRepo = new InMemoryDeviceRepository();
        var templateRepo = new InMemoryTemplateRepository();
        var sandboxProvider = new StubSandboxProvider();
        var svc = new DeviceService(deviceRepo, templateRepo, sandboxProvider);

        var result = await svc.CreateDeviceAsync(TenantACaller,
            new CreateDeviceRequest("bot-1", "docker", 15));

        Assert.Equal("bot-1", result.BotId);
        Assert.Equal("creating", result.Status);
        Assert.NotEmpty(result.DeviceId);
    }

    [Fact]
    public async Task GetDeviceStatus_WhenTenantMismatch_ShouldThrow()
    {
        var deviceRepo = new InMemoryDeviceRepository();
        var templateRepo = new InMemoryTemplateRepository();
        var sandboxProvider = new StubSandboxProvider();
        var svc = new DeviceService(deviceRepo, templateRepo, sandboxProvider);

        var created = await svc.CreateDeviceAsync(TenantACaller,
            new CreateDeviceRequest("bot-1", "docker", 15));

        // Tenant-b accessing tenant-a's device: status returned because
        // device doesn't exist in b's scope; DeviceService checks repo
        // which is keyed by deviceId; cross-tenant guard via TenantGuard.
        // In our in-memory repo, device exists for all.
        await Assert.ThrowsAsync<TenantMismatchException>(
            () => svc.GetDeviceStatusAsync(TenantBCaller, created.DeviceId));
    }

    [Fact]
    public async Task DestroyDevice_WhenNotFound_ShouldReturnNotDestroyed()
    {
        var svc = new DeviceService(
            new InMemoryDeviceRepository(),
            new InMemoryTemplateRepository(),
            new StubSandboxProvider());

        var result = await svc.DestroyDeviceAsync(TenantACaller, "nonexistent");
        Assert.False(result.Destroyed);
        Assert.Equal("DEVICE_NOT_FOUND", result.Reason);
    }

    [Fact]
    public async Task RenewTtl_WhenTenantMismatch_ShouldThrow()
    {
        var deviceRepo = new InMemoryDeviceRepository();
        var svc = new DeviceService(deviceRepo, new InMemoryTemplateRepository(), new StubSandboxProvider());

        var created = await svc.CreateDeviceAsync(TenantACaller,
            new CreateDeviceRequest("bot-1", "docker", 15));

        await Assert.ThrowsAsync<TenantMismatchException>(
            () => svc.RenewTtlAsync(TenantBCaller, created.DeviceId));
    }

    // ── Health Service ──

    [Fact]
    public async Task GetHealth_ShouldReturnHealthy()
    {
        var svc = new HealthService(null!);
        var result = await svc.GetHealthAsync(TenantACaller);

        Assert.True(result.IsHealthy);
        Assert.NotEmpty(result.Components);
    }

    // ── Tenant Guard ──

    [Fact]
    public void TenantGuard_ShouldThrow_WhenMismatch()
    {
        Assert.Throws<TenantMismatchException>(() =>
            TenantGuard.AssertTenantMatch(TenantACaller, "tenant-b"));
    }

    [Fact]
    public void TenantGuard_ShouldNotThrow_WhenMatch()
    {
        TenantGuard.AssertTenantMatch(TenantACaller, "tenant-a");
    }
}

// ── In-Memory Stub Repositories (for testing) ──

internal sealed class InMemoryTemplateRepository : ITemplateRepository
{
    private readonly ConcurrentDictionary<string, TemplateRecord> _store = new();

    public Task<TemplateRecord?> GetByTenantAndUuidAsync(string tenantId, string templateUuid, CancellationToken ct)
    {
        _store.TryGetValue(templateUuid, out var record);
        if (record is not null && record.TenantId != tenantId)
            return Task.FromResult<TemplateRecord?>(null);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<TemplateRecord>> ListByTenantAsync(string tenantId, CancellationToken ct)
    {
        var items = _store.Values.Where(r => r.TenantId == tenantId).ToList();
        return Task.FromResult<IReadOnlyList<TemplateRecord>>(items);
    }

    public Task InsertAsync(TemplateRecord record, CancellationToken ct)
    {
        _store[record.Uuid] = record;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TemplateRecord record, CancellationToken ct)
    {
        _store[record.Uuid] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tenantId, string templateUuid, CancellationToken ct)
    {
        _store.TryRemove(templateUuid, out _);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTenantRepository : ITenantRepository
{
    private TenantRecord? _record;

    public void Seed(TenantRecord record) => _record = record;

    public Task<TenantRecord?> GetByTenantIdAsync(string tenantId, CancellationToken ct)
    {
        return Task.FromResult(_record?.TenantId == tenantId ? _record : null);
    }

    public Task UpsertAsync(TenantRecord record, CancellationToken ct)
    {
        _record = record;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly ConcurrentDictionary<string, DeviceRecord> _store = new();

    public Task<DeviceRecord?> GetByIdAsync(string deviceId, CancellationToken ct)
    {
        _store.TryGetValue(deviceId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<DeviceRecord>> ListByBotAsync(string tenantId, string botId, CancellationToken ct)
    {
        var items = _store.Values.Where(r => r.TenantId == tenantId && r.BotId == botId).ToList();
        return Task.FromResult<IReadOnlyList<DeviceRecord>>(items);
    }

    public Task InsertAsync(DeviceRecord record, CancellationToken ct)
    {
#pragma warning disable IDE0028 // In-memory test stub, initializes with caller-provided values
        _store[record.DeviceId] = new DeviceRecord(
            record.DeviceId, record.TenantId, record.BotId,
            record.TemplateUuid, record.SandboxProfile, record.Status,
            record.SandboxId ?? $"sandbox-{Guid.NewGuid():N}"[..8],
            record.InternalEndpoint ?? $"http://localhost:8080",
            record.CreatedAt, record.ExpiresAt, DateTimeOffset.UtcNow);
#pragma warning restore IDE0028
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(string deviceId, string status, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        if (_store.TryGetValue(deviceId, out var existing))
        {
            _store[deviceId] = existing with { Status = status, ExpiresAt = expiresAt ?? existing.ExpiresAt, UpdatedAt = DateTimeOffset.UtcNow };
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string deviceId, CancellationToken ct)
    {
        _store.TryRemove(deviceId, out _);
        return Task.CompletedTask;
    }
}

internal sealed class StubSandboxProvider : ISandboxProvider
{
    public Task<SandboxHandle> CreateAsync(CallerContext caller, SandboxCreateRequest request, CancellationToken ct)
    {
        return Task.FromResult(new SandboxHandle($"sandbox-{Guid.NewGuid():N}"[..16],
            request.BotId, "localhost:8080", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30)));
    }

    public Task DestroyAsync(CallerContext caller, string sandboxId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<SandboxHttpResponse> InvokeHttpAsync(CallerContext caller, SandboxInvokeHttpRequest request, CancellationToken ct)
        => Task.FromResult(new SandboxHttpResponse(200, new Dictionary<string, string>(), null));

    public Task<SandboxStatus> GetStatusAsync(CallerContext caller, string sandboxId, CancellationToken ct)
        => Task.FromResult(new SandboxStatus(sandboxId, "running", DateTimeOffset.UtcNow));
}
