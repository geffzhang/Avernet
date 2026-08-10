using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Ocb.Infrastructure.Vector.Conformance;
using Ocb.Infrastructure.Vector.Qdrant;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Tests.Vector.Qdrant;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class QdrantConformanceTests
{
    [Fact]
    public void SingleboxProfile_WithQdrantConfig_MustFailClosed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new QdrantVectorStore("singlebox"));
        Assert.Contains("cluster", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Qdrant_AdminEnsureCollection_ShouldSucceed_InCluster()
    {
        var admin = new QdrantVectorStoreAdministration("cluster");
        await admin.EnsureCollectionAsync(
            new VectorCollectionSpec("t1", "collection", 1024, "euclidean"), CancellationToken.None);

        var meta = await admin.GetCollectionMetaAsync("t1", "collection", CancellationToken.None);
        Assert.NotNull(meta);
        Assert.Equal(1024, meta!.Dimension);
        Assert.Equal("euclidean", meta.Distance);
    }

    [Fact]
    public async Task Qdrant_PassesFullConformanceSuite()
    {
        var collections = new ConcurrentDictionary<string, CollectionMeta>();

        var store = new QdrantVectorStore("cluster", collections);
        var admin = new QdrantVectorStoreAdministration("cluster", collections);

        await VectorStoreConformanceSuite.AssertUpsertAndSearchRoundtripAsync(store, CancellationToken.None);
        await VectorStoreConformanceSuite.AssertGetReturnsUpsertedRecordAsync(store, CancellationToken.None);
        await VectorStoreConformanceSuite.AssertDeleteRemovesRecordAsync(store, CancellationToken.None);
        await VectorStoreConformanceSuite.AssertFailClosedOnModelMismatchAsync(store, admin, CancellationToken.None);
        await VectorStoreConformanceSuite.AssertAdminCreateAndGetCollectionMetaAsync(admin, CancellationToken.None);
    }

    [Fact]
    public async Task Qdrant_AdminDeleteCollection_RemovesMeta()
    {
        var admin = new QdrantVectorStoreAdministration("cluster");
        await admin.EnsureCollectionAsync(
            new VectorCollectionSpec("t1", "coll", 256, "cosine"), CancellationToken.None);

        await admin.DeleteCollectionAsync("t1", "coll", CancellationToken.None);

        var meta = await admin.GetCollectionMetaAsync("t1", "coll", CancellationToken.None);
        Assert.Null(meta);
    }
}
