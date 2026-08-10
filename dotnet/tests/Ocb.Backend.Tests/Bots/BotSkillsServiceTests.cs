using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Bots;
using Ocb.Contracts;
using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;

using CallerCtx = Ocb.Contracts.CallerContext;

namespace Ocb.Backend.Tests.Bots;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BotSkillsServiceTests
{
    private readonly FakeSkillPublicationStore _store = new();
    private readonly BotSkillsService _service;

    public BotSkillsServiceTests()
    {
        _service = new BotSkillsService(_store);
    }

    [Fact]
    public async Task BuildReconciliationCommandAsync_ReturnsCommand_WhenSkillsArePublished()
    {
        _store.AddPublished("tenant-a", "bot-1", "skill-1", "center://uuid-1", "1.0");
        _store.AddPublished("tenant-a", "bot-1", "skill-2", "center://uuid-2", "2.0");

        var request = new SkillActivationRequest(
            Caller: new CallerCtx("tenant-a", "u-1", new HashSet<string>(StringComparer.Ordinal) { "user" }),
            BotId: "bot-1",
            SkillIds: ["skill-1", "skill-2"],
            ManifestContractVersion: "skills-pool-p3-v1");

        var command = await _service.BuildReconciliationCommandAsync(request, CancellationToken.None);

        Assert.Equal("tenant-a", command.TenantId);
        Assert.Equal("bot-1", command.BotId);
        Assert.Equal("u-1", command.SubjectId);
        Assert.Equal(["skill-1", "skill-2"], command.SkillIds);
        Assert.Equal("skills-pool-p3-v1", command.ManifestContractVersion);
    }

    [Fact]
    public async Task BuildReconciliationCommandAsync_Throws_WhenSkillsNotPublished()
    {
        _store.AddPublished("tenant-a", "bot-1", "skill-1", "center://uuid-1", "1.0");

        var request = new SkillActivationRequest(
            Caller: new CallerCtx("tenant-a", "u-1", new HashSet<string>(StringComparer.Ordinal) { "user" }),
            BotId: "bot-1",
            SkillIds: ["skill-1", "unpublished-skill"],
            ManifestContractVersion: "skills-pool-p3-v1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.BuildReconciliationCommandAsync(request, CancellationToken.None));

        Assert.Contains("not published", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unpublished-skill", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildReconciliationCommandAsync_Throws_OnNullRequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.BuildReconciliationCommandAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task BuildReconciliationCommandAsync_Throws_OnNullCaller()
    {
        var request = new SkillActivationRequest(
            Caller: null!,
            BotId: "bot-1",
            SkillIds: ["skill-1"],
            ManifestContractVersion: "skills-pool-p3-v1");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.BuildReconciliationCommandAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task BuildReconciliationCommandAsync_Succeeds_WhenEmptySkillList()
    {
        var request = new SkillActivationRequest(
            Caller: new CallerCtx("tenant-a", "u-1", new HashSet<string>(StringComparer.Ordinal)),
            BotId: "bot-1",
            SkillIds: [],
            ManifestContractVersion: "skills-pool-p3-v1");

        var command = await _service.BuildReconciliationCommandAsync(request, CancellationToken.None);

        Assert.Empty(command.SkillIds);
    }

    private sealed class FakeSkillPublicationStore : ISkillPublicationStorePlugin
    {
        private readonly List<SkillPublicationRecord> _records = [];

        public void AddPublished(
            string tenantId, string botId, string skillId, string sourceLocator, string version)
        {
            _records.Add(new SkillPublicationRecord(
                tenantId, botId, skillId,
                new SkillVersionRef(sourceLocator, version, SkillSourceScheme.Center),
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "published", "skills-pool-p3-v1"));
        }

        public Task<SkillPublicationRecord?> GetBySkillAsync(
            string tenantId, string botId, string skillId, CancellationToken ct) =>
            Task.FromResult(_records.SingleOrDefault(r =>
                r.TenantId == tenantId && r.BotId == botId && r.SkillId == skillId));

        public Task<IReadOnlyList<SkillPublicationRecord>> ListByBotAsync(
            string tenantId, string botId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SkillPublicationRecord>>(
                _records.Where(r => r.TenantId == tenantId && r.BotId == botId).ToList());

        public Task InsertAsync(SkillPublicationRecord record, CancellationToken ct)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdatePublicationStateAsync(
            string tenantId, string botId, string skillId, string publicationState, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
