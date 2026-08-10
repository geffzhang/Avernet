using System.Collections.Concurrent;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.SonnetDb;

/// <summary>
/// Internal guard for SonnetDB model/dimension compatibility checks.
/// </summary>
internal static class SonnetDbGuard
{
    public static void ValidateOrThrow(
        string model,
        int dimension,
        string tenantId,
        ConcurrentDictionary<string, CollectionMeta> collections)
    {
        foreach (var kvp in collections)
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
}
