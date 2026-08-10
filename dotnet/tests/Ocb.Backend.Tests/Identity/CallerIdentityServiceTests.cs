using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Identity;
using Ocb.Contracts;
using Ocb.Contracts.Identity;

namespace Ocb.Backend.Tests.Identity;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class CallerIdentityServiceTests
{
    private readonly FakeCallerIdentityRepository _repo = new();
    private readonly CallerIdentityService _service;

    public CallerIdentityServiceTests()
    {
        _service = new CallerIdentityService(_repo);
    }

    [Fact]
    public async Task ResolveCallerAsync_ReturnsBinding_WhenTenantMatches()
    {
        _repo.AddBinding("tenant-a", "bot-1", "u-1", new HashSet<string>(StringComparer.Ordinal) { "admin" });
        var context = new CallerContext("tenant-a", "u-1", new HashSet<string>(StringComparer.Ordinal) { "admin" });

        var binding = await _service.ResolveCallerAsync(context, "bot-1", CancellationToken.None);

        Assert.Equal("tenant-a", binding.TenantId);
        Assert.Equal("bot-1", binding.BotId);
        Assert.Equal("u-1", binding.SubjectId);
    }

    [Fact]
    public async Task ResolveCallerAsync_Throws_WhenBindingNotFound()
    {
        var context = new CallerContext("tenant-x", "u-1", new HashSet<string>(StringComparer.Ordinal) { "user" });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ResolveCallerAsync(context, "bot-1", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveCallerAsync_Throws_WhenTenantMismatch()
    {
        // The repo returns a binding for tenant-a, but the caller claims tenant-b.
        // Since lookup uses the caller's tenantId, there's simply no match in the repo.
        _repo.AddBinding("tenant-a", "bot-1", "u-1", new HashSet<string>(StringComparer.Ordinal) { "user" });
        var context = new CallerContext("tenant-b", "u-1", new HashSet<string>(StringComparer.Ordinal) { "user" });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ResolveCallerAsync(context, "bot-1", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveCallerAsync_Throws_OnNullContext()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ResolveCallerAsync(null!, "bot-1", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveCallerAsync_Throws_OnEmptyBotId()
    {
        var context = new CallerContext("t1", "u1", new HashSet<string>(StringComparer.Ordinal));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ResolveCallerAsync(context, "", CancellationToken.None));
    }

    private sealed class FakeCallerIdentityRepository : Ocb.PluginApi.Identity.ICallerIdentityRepositoryPlugin
    {
        private readonly List<CallerIdentityBinding> _bindings = [];

        public void AddBinding(
            string tenantId, string botId, string subjectId, IReadOnlySet<string> roles) =>
            _bindings.Add(new CallerIdentityBinding(tenantId, botId, subjectId, roles));

        public Task<CallerIdentityBinding?> GetCallerIdentityAsync(
            string tenantId, string botId, string subjectId, CancellationToken ct) =>
            Task.FromResult(_bindings.SingleOrDefault(b =>
                b.TenantId == tenantId && b.BotId == botId && b.SubjectId == subjectId));

        public Task<IReadOnlyList<CallerIdentityBinding>> ListByBotAsync(
            string tenantId, string botId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CallerIdentityBinding>>(
                _bindings.Where(b => b.TenantId == tenantId && b.BotId == botId).ToList());
    }
}
