using Ocb.PluginApi.Vector;

namespace Ocb.Plugins.Embedding;

/// <summary>
/// Default embedding plugin — generates deterministic mock embeddings for
/// development and testing. Replace with a real embedding provider (e.g.,
/// sentence-transformers, OpenAI embeddings) in production.
/// </summary>
public sealed class DefaultEmbeddingPlugin : IEmbeddingPlugin
{
    /// <summary>
    /// Generate mock embeddings of fixed dimension for each input text.
    /// Uses the text hash to produce repeatable results.
    /// </summary>
    public ValueTask<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        string model,
        CancellationToken ct)
    {
        const int dimension = 1024;

        var results = new List<float[]>(inputs.Count);
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();

            var embedding = new float[dimension];
            var hash = input.GetHashCode(StringComparison.Ordinal);

            // Deterministic embedding based on text hash
            var rng = new Random(unchecked(hash * 31 + model.GetHashCode(StringComparison.Ordinal)));
            for (var i = 0; i < dimension; i++)
            {
                embedding[i] = (float)(rng.NextDouble() * 2 - 1);
            }

            // Normalize to unit length
            var norm = MathF.Sqrt(embedding.Sum(x => x * x));
            if (norm > 0)
            {
                for (var i = 0; i < dimension; i++)
                {
                    embedding[i] /= norm;
                }
            }

            results.Add(embedding);
        }

        return ValueTask.FromResult<IReadOnlyList<float[]>>(results);
    }
}
