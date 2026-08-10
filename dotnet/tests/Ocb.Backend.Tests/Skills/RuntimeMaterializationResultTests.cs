using System.Diagnostics.CodeAnalysis;
using Ocb.Backend.Skills.Activation;
using Ocb.Contracts.Skills;
using Ocb.PluginApi.Skills;
using CallerCtx = Ocb.Contracts.CallerContext;

namespace Ocb.Backend.Tests.Skills;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class RuntimeMaterializationResultTests
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
    public async Task IntegrityFailure_IsRecordedWithoutBackendRetry()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new MaterializationResult(false, "failed", null, "INTEGRITY_CHECK_FAILED"),
        };
        var store = new FakeObservedStore();
        var service = new SkillActivationService(CreateFakeStore(), runtime, store);

        var result = await service.ActivateAsync(Caller, "b1", ["s1"], "skills-pool-p3-v1", CancellationToken.None);

        Assert.Equal(1, runtime.CallCount); // no backend retry
        Assert.Equal("INTEGRITY_CHECK_FAILED", result.ErrorCode);
        Assert.Equal("INTEGRITY_CHECK_FAILED", store.ErrorCode);
    }

    [Fact]
    public async Task SuccessfulMaterialization_RecordsObservedState()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new MaterializationResult(true, "active", "view-1", null),
        };
        var store = new FakeObservedStore();
        var service = new SkillActivationService(CreateFakeStore(), runtime, store);

        var result = await service.ActivateAsync(Caller, "b1", ["s1"], "skills-pool-p3-v1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("active", store.ObservedState);
        Assert.Equal("view-1", store.ActiveViewId);
        Assert.Null(store.ErrorCode);
    }

    [Fact]
    public async Task RuntimeIsCalledExactlyOnce_PerActivation()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new MaterializationResult(true, "active", "v1", null),
        };
        var service = new SkillActivationService(CreateFakeStore(), runtime, new FakeObservedStore());

        await service.ActivateAsync(Caller, "b1", ["s1"], "skills-pool-p3-v1", CancellationToken.None);

        Assert.Equal(1, runtime.CallCount);
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
