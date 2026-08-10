using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.Guards;

/// <summary>
/// Public guard for model/dimension compatibility checks.
/// Validates that embedding model and dimension match the collection
/// metadata before performing vector operations.
/// </summary>
public static class VectorCompatibilityGuard
{
    /// <summary>
    /// Validate that the requested model and dimension match the collection.
    /// Throws <see cref="VectorCompatibilityException"/> on mismatch.
    /// </summary>
    /// <param name="model">The requested embedding model.</param>
    /// <param name="dimension">The requested embedding dimension.</param>
    /// <param name="meta">The collection metadata to validate against.</param>
    /// <exception cref="VectorCompatibilityException">
    /// Thrown when model or dimension does not match the collection.
    /// </exception>
    public static void ValidateOrThrow(string model, int dimension, CollectionMeta meta)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(meta);

        if (!string.Equals(model, meta.Model, StringComparison.Ordinal))
        {
            throw new VectorCompatibilityException(
                $"Embedding model mismatch: requested '{model}', collection has '{meta.Model}'. " +
                $"All vectors in a collection must use the same embedding model.");
        }

        if (dimension != meta.Dimension)
        {
            throw new VectorCompatibilityException(
                $"Embedding dimension mismatch: requested {dimension}, collection has {meta.Dimension}. " +
                $"All vectors in a collection must have the same dimension.");
        }
    }
}
