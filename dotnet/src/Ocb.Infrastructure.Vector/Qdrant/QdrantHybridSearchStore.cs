using System.Collections.Concurrent;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.Qdrant;

/// <summary>
/// Qdrant hybrid search store — cluster deployment only.
/// </summary>
public sealed class QdrantHybridSearchStore : IHybridSearchStore
{
    private readonly string _profile;
    private readonly ConcurrentDictionary<(string, string), VectorRecord> _records;
    private readonly ConcurrentDictionary<string, CollectionMeta> _collections;

    public QdrantHybridSearchStore(
        string profile,
        ConcurrentDictionary<(string, string), VectorRecord> records,
        ConcurrentDictionary<string, CollectionMeta>? collections = null)
    {
        if (!string.Equals(profile, "cluster", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Qdrant is cluster-only.");

        _profile = profile;
        _records = records;
        _collections = collections ?? new ConcurrentDictionary<string, CollectionMeta>();
    }

    public ValueTask<IReadOnlyList<VectorHit>> SearchAsync(HybridSearchQuery query, CancellationToken ct)
    {
        QdrantGuard.ValidateOrThrow(query.Model, query.Dimension, query.TenantId, _collections);

        var hits = _records.Values
            .Where(r => r.TenantId == query.TenantId)
            .Select(r => new VectorHit(r.VectorId,
                CosineSimilarity(r.Embedding, query.QueryEmbedding) * query.VectorWeight +
                KeywordScore(r, query.KeywordQuery) * (1 - query.VectorWeight),
                r.Payload))
            .OrderByDescending(h => h.Score)
            .Take(query.TopK)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<VectorHit>>(hits);
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

    private static float KeywordScore(VectorRecord record, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || record.Payload is null)
            return 0f;
        return record.Payload.Values.Any(v =>
            v.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ? 1.0f : 0f;
    }
}
