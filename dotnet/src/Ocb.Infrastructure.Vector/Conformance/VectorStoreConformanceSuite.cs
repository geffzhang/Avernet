using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.Conformance;

/// <summary>
/// Shared conformance suite for vector store providers.
/// Both SonnetDB and Qdrant must pass all these cases.
/// Methods throw on failure so any test runner can detect violations.
/// </summary>
public static class VectorStoreConformanceSuite
{
    public static async Task AssertFailClosedOnModelMismatchAsync(
        IVectorStore store,
        IVectorStoreAdministration admin,
        CancellationToken ct)
    {
        var tenantId = "tenant-conformance";
        await admin.EnsureCollectionAsync(
            new VectorCollectionSpec(tenantId, "collection", 1024, "cosine"), ct);

        await store.UpsertAsync(
            new VectorRecord(tenantId, "id1", "model-a", 1024, new float[1024], null), ct);

        try
        {
            await store.SearchAsync(
                new VectorQuery(tenantId, "model-b", 768, new float[768], 5), ct);
            throw new InvalidOperationException("Expected VectorCompatibilityException was not thrown for model mismatch.");
        }
        catch (VectorCompatibilityException)
        {
            // Expected — fail closed on model mismatch
        }
    }

    public static async Task AssertFailClosedOnDimensionMismatchAsync(
        IVectorStore store,
        IVectorStoreAdministration admin,
        CancellationToken ct)
    {
        var tenantId = "tenant-dim";
        await admin.EnsureCollectionAsync(
            new VectorCollectionSpec(tenantId, "collection", 1024, "cosine"), ct);

        await store.UpsertAsync(
            new VectorRecord(tenantId, "id1", "model-a", 1024, new float[1024], null), ct);

        try
        {
            await store.SearchAsync(
                new VectorQuery(tenantId, "model-a", 512, new float[512], 5), ct);
            throw new InvalidOperationException("Expected VectorCompatibilityException was not thrown for dimension mismatch.");
        }
        catch (VectorCompatibilityException)
        {
            // Expected — fail closed on dimension mismatch
        }
    }

    public static async Task AssertUpsertAndSearchRoundtripAsync(
        IVectorStore store,
        CancellationToken ct)
    {
        var tenantId = "tenant-roundtrip";
        var embedding = GenerateTestEmbedding(128);

        await store.UpsertAsync(
            new VectorRecord(tenantId, "id-r1", "model-x", 128, embedding, null), ct);

        var results = await store.SearchAsync(
            new VectorQuery(tenantId, "model-x", 128, embedding, 5), ct);

        if (results.Count == 0)
            throw new InvalidOperationException("Search returned no results after upsert.");
        if (!results.Any(h => h.VectorId == "id-r1"))
            throw new InvalidOperationException("Search results do not contain the upserted record.");
    }

    public static async Task AssertGetReturnsUpsertedRecordAsync(
        IVectorStore store,
        CancellationToken ct)
    {
        var tenantId = "tenant-get";
        var embedding = GenerateTestEmbedding(64);

        await store.UpsertAsync(
            new VectorRecord(tenantId, "id-g1", "model-g", 64, embedding, null), ct);

        var record = await store.GetAsync(tenantId, "id-g1", ct);

        if (record is null)
            throw new InvalidOperationException("Get did not return the upserted record.");
        if (record.VectorId != "id-g1")
            throw new InvalidOperationException($"Expected VectorId 'id-g1' but got '{record.VectorId}'.");
        if (record.Model != "model-g")
            throw new InvalidOperationException($"Expected Model 'model-g' but got '{record.Model}'.");
        if (record.Dimension != 64)
            throw new InvalidOperationException($"Expected Dimension 64 but got {record.Dimension}.");
    }

    public static async Task AssertDeleteRemovesRecordAsync(
        IVectorStore store,
        CancellationToken ct)
    {
        var tenantId = "tenant-delete";
        var embedding = GenerateTestEmbedding(64);

        await store.UpsertAsync(
            new VectorRecord(tenantId, "id-d1", "model-d", 64, embedding, null), ct);

        await store.DeleteAsync(tenantId, "id-d1", ct);

        var record = await store.GetAsync(tenantId, "id-d1", ct);
        if (record is not null)
            throw new InvalidOperationException("Delete did not remove the record — Get still returns it.");
    }

    public static async Task AssertHybridSearchReturnsResultsAsync(
        IHybridSearchStore hybridStore,
        IVectorStore store,
        CancellationToken ct)
    {
        var tenantId = "tenant-hybrid";
        var embedding = GenerateTestEmbedding(128);

        await store.UpsertAsync(
            new VectorRecord(tenantId, "id-h1", "model-h", 128, embedding,
                new Dictionary<string, string> { ["type"] = "doc" }), ct);

        var results = await hybridStore.SearchAsync(
            new HybridSearchQuery(tenantId, "model-h", 128, embedding, "test", 5), ct);

        if (results.Count == 0)
            throw new InvalidOperationException("Hybrid search returned no results after upsert.");
    }

    public static async Task AssertAdminCreateAndGetCollectionMetaAsync(
        IVectorStoreAdministration admin,
        CancellationToken ct)
    {
        var tenantId = "tenant-admin";
        await admin.EnsureCollectionAsync(
            new VectorCollectionSpec(tenantId, "admin-collection", 768, "cosine"), ct);

        var meta = await admin.GetCollectionMetaAsync(tenantId, "admin-collection", ct);

        if (meta is null)
            throw new InvalidOperationException("GetCollectionMeta returned null after EnsureCollection.");
        if (meta.Dimension != 768)
            throw new InvalidOperationException($"Expected Dimension 768 but got {meta.Dimension}.");
    }

    public static float[] GenerateTestEmbedding(int dimension)
    {
        var embedding = new float[dimension];
        for (int i = 0; i < dimension; i++)
            embedding[i] = (float)i / dimension;
        return embedding;
    }
}
