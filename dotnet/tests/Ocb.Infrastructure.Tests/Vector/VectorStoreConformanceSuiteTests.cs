using System.Diagnostics.CodeAnalysis;
using Ocb.Infrastructure.Tests.Vector.Fixtures;
using Ocb.Infrastructure.Vector.Conformance;

namespace Ocb.Infrastructure.Tests.Vector;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class VectorStoreConformanceSuiteTests
{
    private readonly FakeVectorStore _store = new();

    [Fact]
    public async Task Conformance_MustFailClosed_OnModelDimensionMismatch()
    {
        await VectorStoreConformanceSuite.AssertFailClosedOnModelMismatchAsync(_store, _store, CancellationToken.None);
    }

    [Fact]
    public async Task Conformance_MustFailClosed_OnDimensionMismatch()
    {
        await VectorStoreConformanceSuite.AssertFailClosedOnDimensionMismatchAsync(_store, _store, CancellationToken.None);
    }

    [Fact]
    public async Task Conformance_UpsertAndSearch_Roundtrip()
    {
        await VectorStoreConformanceSuite.AssertUpsertAndSearchRoundtripAsync(_store, CancellationToken.None);
    }

    [Fact]
    public async Task Conformance_Get_ReturnsUpsertedRecord()
    {
        await VectorStoreConformanceSuite.AssertGetReturnsUpsertedRecordAsync(_store, CancellationToken.None);
    }

    [Fact]
    public async Task Conformance_Delete_RemovesRecord()
    {
        await VectorStoreConformanceSuite.AssertDeleteRemovesRecordAsync(_store, CancellationToken.None);
    }

    [Fact]
    public async Task Conformance_HybridSearch_ReturnsResults()
    {
        await VectorStoreConformanceSuite.AssertHybridSearchReturnsResultsAsync(_store, _store, CancellationToken.None);
    }

    [Fact]
    public async Task Conformance_Admin_CreateAndGetCollectionMeta()
    {
        await VectorStoreConformanceSuite.AssertAdminCreateAndGetCollectionMetaAsync(_store, CancellationToken.None);
    }
}
