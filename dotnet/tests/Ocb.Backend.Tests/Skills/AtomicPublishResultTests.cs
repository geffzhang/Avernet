using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Activation;
using Ocb.Backend.Skills.Errors;
using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;
using CallerCtx = Ocb.Contracts.CallerContext;

namespace Ocb.Backend.Tests.Skills;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class AtomicPublishResultTests
{
    private sealed class FakeRuntime : IRuntimeSkillMaterializationService
    {
        public int CallCount { get; private set; }
        public MaterializationResult? NextResult { get; set; }

        public Task<MaterializationResult> MaterializeActivatedSkillsAsync(
            MaterializationRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(NextResult!);
        }
    }

    private sealed class FakeObservedStore : IObservedStateStore
    {
        public string? ObservedState { get; private set; }
        public string? ActiveViewId { get; private set; }
        public string? ErrorCode { get; private set; }

        public Task RecordAsync(string tenantId, string botId, string observedState,
            string? activeViewId, string? errorCode, CancellationToken ct)
        {
            ObservedState = observedState;
            ActiveViewId = activeViewId;
            ErrorCode = errorCode;
            return Task.CompletedTask;
        }
    }

    private static CallerCtx Caller => new("t1", "u1", new HashSet<string> { "user" });

    [Fact]
    public async Task RuntimeRollbackResult_PreservesPreviousObservedView()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new MaterializationResult(false, "failed_rolled_back", "view-prev", "ATOMIC_PUBLISH_FAILED"),
        };
        var store = new FakeObservedStore();
        var service = new SkillActivationService(CreateFakeStore(), runtime, store);

        await service.ActivateAsync(Caller, "b1", ["s1"], "skills-pool-p3-v1", CancellationToken.None);

        Assert.Equal("view-prev", store.ActiveViewId);
        Assert.Equal("failed_rolled_back", store.ObservedState);
        Assert.Equal("ATOMIC_PUBLISH_FAILED", store.ErrorCode);
    }

    [Fact]
    public async Task PublishFailure_WithoutPreviousView_FailsClosed()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new MaterializationResult(false, "failed", null, "ACTIVATION_NO_PREVIOUS_VIEW"),
        };
        var service = new SkillActivationService(CreateFakeStore(), runtime, new FakeObservedStore());

        var ex = await Assert.ThrowsAsync<ActivationFailClosedException>(() =>
            service.ActivateAsync(Caller, "b1", ["s1"], "skills-pool-p3-v1", CancellationToken.None));

        Assert.Contains("no previous view", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulPublish_PreservesActiveView()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new MaterializationResult(true, "active", "view-5", null),
        };
        var store = new FakeObservedStore();
        var service = new SkillActivationService(CreateFakeStore(), runtime, store);

        var result = await service.ActivateAsync(Caller, "b1", ["s1"], "skills-pool-p3-v1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("view-5", store.ActiveViewId);
        Assert.Equal("active", store.ObservedState);
    }

    private static FakeSkillPubStore CreateFakeStore()
        => new FakeSkillPubStore();

    private sealed class FakeSkillPubStore : ISkillPublicationStorePlugin
    {
        public Task<SkillPublicationRecord?> GetBySkillAsync(
            string tenantId, string botId, string skillId, CancellationToken cancellationToken)
            => Task.FromResult<SkillPublicationRecord?>(null);

        public Task<IReadOnlyList<SkillPublicationRecord>> ListByBotAsync(
            string tenantId, string botId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SkillPublicationRecord>>([]);

        public Task InsertAsync(SkillPublicationRecord record, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdatePublicationStateAsync(
            string tenantId, string botId, string skillId, string publicationState,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
