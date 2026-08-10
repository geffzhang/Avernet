using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Publication;
using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;

namespace Ocb.Backend.Tests.Skills;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PublicationServiceTests
{
    private sealed class FakeSkillPublicationStore : ISkillPublicationStorePlugin
    {
        private readonly List<SkillPublicationRecord> _records = [];

        public SkillPublicationRecord? ReturnedRecord { get; set; }

        public Task<SkillPublicationRecord?> GetBySkillAsync(
            string tenantId, string botId, string skillId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReturnedRecord);
        }

        public Task<IReadOnlyList<SkillPublicationRecord>> ListByBotAsync(
            string tenantId, string botId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SkillPublicationRecord>>(_records);
        }

        public Task InsertAsync(SkillPublicationRecord record, CancellationToken cancellationToken)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdatePublicationStateAsync(
            string tenantId, string botId, string skillId, string publicationState,
            CancellationToken cancellationToken)
        {
            var rec = _records.FirstOrDefault(r =>
                r.TenantId == tenantId && r.BotId == botId && r.SkillId == skillId);
            if (rec is not null)
            {
                _records.Remove(rec);
                _records.Add(rec with { PublicationState = publicationState });
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CenterSource_WithoutPublishedState_IsRejected()
    {
        var store = new FakeSkillPublicationStore { ReturnedRecord = null };
        var service = new SkillPublicationService(store);
        var version = new SkillVersionRef("center://uuid-1", "3", SkillSourceScheme.Center);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishImmutableVersionAsync("tenant-a", "bot-1", "skill-1", version,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CancellationToken.None));

        // In newer .NET, the exception message may be localized. Check for the key term.
        Assert.True(
            ex.Message.Contains("center://", StringComparison.Ordinal) ||
            ex.Message.Contains("center", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            ex.Message.Contains("published", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("published", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CenterSource_WithNonPublishedState_IsRejected()
    {
        var store = new FakeSkillPublicationStore
        {
            ReturnedRecord = new SkillPublicationRecord(
                "tenant-a", "bot-1", "skill-1",
                new SkillVersionRef("center://uuid-old", "2", SkillSourceScheme.Center),
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "draft", "skills-pool-p3-v1"),
        };
        var service = new SkillPublicationService(store);
        var version = new SkillVersionRef("center://uuid-1", "3", SkillSourceScheme.Center);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishImmutableVersionAsync("tenant-a", "bot-1", "skill-1", version,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CancellationToken.None));

        Assert.Contains("published", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CenterSource_WithPublishedState_Succeeds()
    {
        var store = new FakeSkillPublicationStore
        {
            ReturnedRecord = new SkillPublicationRecord(
                "tenant-a", "bot-1", "skill-1",
                new SkillVersionRef("git://repo", "2", SkillSourceScheme.Git),
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "published", "skills-pool-p3-v1"),
        };
        var service = new SkillPublicationService(store);
        var version = new SkillVersionRef("center://uuid-1", "3", SkillSourceScheme.Center);

        var record = await service.PublishImmutableVersionAsync(
            "tenant-a", "bot-1", "skill-1", version,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CancellationToken.None);

        Assert.Equal("published", record.PublicationState);
        Assert.Equal("center://uuid-1", record.Version.SourceLocator);
        Assert.Equal("3", record.Version.ImmutableVersion);
    }

    [Fact]
    public async Task GitSource_PublishCreatesDraftRecord()
    {
        var store = new FakeSkillPublicationStore();
        var service = new SkillPublicationService(store);
        var version = new SkillVersionRef("git://repo/skills", "v1.0", SkillSourceScheme.Git);

        var record = await service.PublishImmutableVersionAsync(
            "tenant-a", "bot-1", "skill-1", version,
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", CancellationToken.None);

        Assert.Equal("draft", record.PublicationState);
        Assert.Equal(SkillSourceScheme.Git, record.Version.Scheme);
        Assert.Equal("v1.0", record.Version.ImmutableVersion);
    }

    [Fact]
    public async Task LocalSource_PublishCreatesDraftRecord()
    {
        var store = new FakeSkillPublicationStore();
        var service = new SkillPublicationService(store);
        var version = new SkillVersionRef("local:///home/dev/skills", "v2.0", SkillSourceScheme.Local);

        var record = await service.PublishImmutableVersionAsync(
            "tenant-x", "bot-2", "skill-7", version,
            "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd", CancellationToken.None);

        Assert.Equal("draft", record.PublicationState);
        Assert.Equal("skills-pool-p3-v1", record.ManifestContractVersion);
        Assert.Equal("tenant-x", record.TenantId);
    }
}
