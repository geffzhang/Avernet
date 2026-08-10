using Ocb.PluginApi.Vector;

namespace Ocb.Plugins.Reranker;

/// <summary>
/// Default reranker plugin — passes through candidates with adjusted scores
/// for development and testing. Replace with a real reranker (e.g., Cohere,
/// cross-encoder) in production.
/// </summary>
public sealed class DefaultRerankerPlugin : IRerankerPlugin
{
    /// <summary>
    /// Rerank candidates by computing a simple TF-IDF-style relevance score
    /// against the query. Returns the top-K results sorted by rerank score.
    /// </summary>
    public ValueTask<IReadOnlyList<RerankHit>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        int topK,
        CancellationToken ct)
    {
        var results = new List<RerankHit>(candidates.Count);

        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = candidates[i];

            // Simple relevance: boost if content contains query terms
            var content = candidate.Content ?? string.Empty;
            var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchCount = queryTerms.Count(term =>
                content.Contains(term, StringComparison.OrdinalIgnoreCase));

            var rerankScore = candidate.InitialScore * (1.0f + matchCount * 0.1f);

            results.Add(new RerankHit(
                VectorId: candidate.VectorId,
                Content: candidate.Content,
                RerankScore: rerankScore,
                Rank: 0 // assigned after sort
            ));
        }

        // Sort by rerank score descending and assign ranks
        var ranked = results
            .OrderByDescending(r => r.RerankScore)
            .Take(topK)
            .Select((hit, index) => hit with { Rank = index + 1 })
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<RerankHit>>(ranked);
    }
}
