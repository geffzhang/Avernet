using System.Collections.Concurrent;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.SonnetDb;

/// <summary>
/// SonnetDB vector store — singlebox deployment only.
/// In-memory stub implementation with profile guard and model/dimension validation.
/// </summary>
public sealed class SonnetDbVectorStore : IVectorStore
{
    private readonly string _profile;
    private readonly ConcurrentDictionary<(string, string), VectorRecord> _records = new();

    private readonly ConcurrentDictionary<string, CollectionMeta> _collections = new();

    public SonnetDbVectorStore(string profile, ConcurrentDictionary<string, CollectionMeta>? collections = null)
    {
        if (!string.Equals(profile, "singlebox", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SonnetDB is singlebox-only. Use Qdrant for cluster deployments.");

        _profile = profile;
        _collections = collections ?? new ConcurrentDictionary<string, CollectionMeta>();
    }

    public ValueTask UpsertAsync(VectorRecord record, CancellationToken ct)
    {
        _records[(record.TenantId, record.VectorId)] = record;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct)
    {
        SonnetDbGuard.ValidateOrThrow(query.Model, query.Dimension, query.TenantId, _collections);

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

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0f;
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
