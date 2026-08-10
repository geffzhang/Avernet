using System.Collections.Concurrent;
using Ocb.PluginApi.Vector;

namespace Ocb.Infrastructure.Vector.SonnetDb;

/// <summary>
/// SonnetDB vector store administration — collection lifecycle management.
/// Singlebox deployment only.
/// </summary>
public sealed class SonnetDbVectorStoreAdministration : IVectorStoreAdministration
{
    private readonly string _profile;
    private readonly ConcurrentDictionary<string, CollectionMeta> _collections;

    public SonnetDbVectorStoreAdministration(
        string profile,
        ConcurrentDictionary<string, CollectionMeta>? collections = null)
    {
        if (!string.Equals(profile, "singlebox", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SonnetDB is singlebox-only. Use Qdrant for cluster deployments.");

        _profile = profile;
        _collections = collections ?? new ConcurrentDictionary<string, CollectionMeta>();
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

    internal ConcurrentDictionary<string, CollectionMeta> GetCollections() => _collections;

    private static string CollectionKey(string tenantId, string collectionName) =>
        $"{tenantId}:{collectionName}";
}
