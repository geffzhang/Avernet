using System.Collections.Concurrent;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.SonnetDb;

/// <summary>
/// SonnetDB hybrid search store — combines vector similarity with keyword filtering.
/// Singlebox deployment only.
/// </summary>
public sealed class SonnetDbHybridSearchStore : IHybridSearchStore
{
    private readonly string _profile;
    private readonly ConcurrentDictionary<(string, string), VectorRecord> _records;
    private readonly ConcurrentDictionary<string, CollectionMeta> _collections;

    public SonnetDbHybridSearchStore(
        string profile,
        ConcurrentDictionary<(string, string), VectorRecord> records,
        ConcurrentDictionary<string, CollectionMeta>? collections = null)
    {
        if (!string.Equals(profile, "singlebox", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SonnetDB is singlebox-only.");

        _profile = profile;
        _records = records;
        _collections = collections ?? new ConcurrentDictionary<string, CollectionMeta>();
    }

    public ValueTask<IReadOnlyList<VectorHit>> SearchAsync(HybridSearchQuery query, CancellationToken ct)
    {
        SonnetDbGuard.ValidateOrThrow(query.Model, query.Dimension, query.TenantId, _collections);

        // Stub: treat keyword query as filter pass-through
        var hits = _records.Values
            .Where(r =>
            {
                if (r.TenantId != query.TenantId) return false;
                // Simple keyword filter: if keyword query present and payload has "type", match it
                if (!string.IsNullOrWhiteSpace(query.KeywordQuery) && r.Payload is not null)
                {
                    return r.Payload.Values.Any(v =>
                        v.Contains(query.KeywordQuery!, StringComparison.OrdinalIgnoreCase));
                }
                return true;
            })
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
