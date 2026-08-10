using System.Collections.Concurrent;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Tests.Vector.Fixtures;

/// <summary>
/// In-memory fake vector store for testing the conformance suite.
/// Enforces model/dimension compatibility checks.
/// </summary>
public sealed class FakeVectorStore : IVectorStore, IHybridSearchStore, IVectorStoreAdministration
{
    private readonly ConcurrentDictionary<string, CollectionMeta> _collections = new();
    private readonly ConcurrentDictionary<(string, string), VectorRecord> _records = new();

    private static string CollectionKey(string tenantId, string collectionName) =>
        $"{tenantId}:{collectionName}";

    private static (string TenantId, string VectorId) RecordKey(VectorRecord r) =>
        (r.TenantId, r.VectorId);

    public ValueTask UpsertAsync(VectorRecord record, CancellationToken ct)
    {
        _records[RecordKey(record)] = record;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct)
    {
        ValidateOrThrow(query.Model, query.Dimension, query.TenantId);

        // Simple dot-product similarity against all records
        var hits = _records.Values
            .Where(r => r.TenantId == query.TenantId)
            .Select(r => new VectorHit(r.VectorId, CosineSimilarity(r.Embedding, query.QueryEmbedding), r.Payload))
            .OrderByDescending(h => h.Score)
            .Take(query.TopK)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<VectorHit>>(hits);
    }

    public ValueTask<IReadOnlyList<VectorHit>> SearchAsync(HybridSearchQuery query, CancellationToken ct)
    {
        ValidateOrThrow(query.Model, query.Dimension, query.TenantId);

        var hits = _records.Values
            .Where(r => r.TenantId == query.TenantId)
            .Select(r => new VectorHit(r.VectorId, CosineSimilarity(r.Embedding, query.QueryEmbedding), r.Payload))
            .OrderByDescending(h => h.Score)
            .Take(query.TopK)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<VectorHit>>(hits);
    }

    public ValueTask<VectorRecord?> GetAsync(string tenantId, string vectorId, CancellationToken ct)
    {
        _records.TryGetValue((tenantId, vectorId), out var record);
        return ValueTask.FromResult(record);
    }

    public ValueTask DeleteAsync(string tenantId, string vectorId, CancellationToken ct)
    {
        _records.TryRemove((tenantId, vectorId), out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask EnsureCollectionAsync(VectorCollectionSpec spec, CancellationToken ct)
    {
        var key = CollectionKey(spec.TenantId, spec.CollectionName);
        _collections[key] = new CollectionMeta("default", spec.Dimension, spec.Distance);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteCollectionAsync(string tenantId, string collectionName, CancellationToken ct)
    {
        var key = CollectionKey(tenantId, collectionName);
        _collections.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<CollectionMeta?> GetCollectionMetaAsync(string tenantId, string collectionName, CancellationToken ct)
    {
        var key = CollectionKey(tenantId, collectionName);
        _collections.TryGetValue(key, out var meta);
        return ValueTask.FromResult(meta);
    }

    private void ValidateOrThrow(string model, int dimension, string tenantId)
    {
        // Check against any collection for this tenant
        foreach (var kvp in _collections)
        {
            if (kvp.Key.StartsWith($"{tenantId}:", StringComparison.Ordinal))
            {
                if (!string.Equals(model, kvp.Value.Model, StringComparison.Ordinal))
                    throw new VectorCompatibilityException(
                        $"Embedding model mismatch: requested '{model}', collection has '{kvp.Value.Model}'.");
                if (dimension != kvp.Value.Dimension)
                    throw new VectorCompatibilityException(
                        $"Embedding dimension mismatch: requested {dimension}, collection has {kvp.Value.Dimension}.");
                return;
            }
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0f;

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
