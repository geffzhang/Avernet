using Ocb.Contracts;

namespace Ocb.PluginApi.Vector;

/// <summary>
/// Core vector store for upsert and search operations.
/// Must NOT expose embedding or reranking methods — those are separate plugins.
/// </summary>
public interface IVectorStore
{
    /// <summary>Upsert a single vector record.</summary>
    ValueTask UpsertAsync(VectorRecord record, CancellationToken ct);

    /// <summary>Search for nearest neighbors.</summary>
    ValueTask<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct);

    /// <summary>Retrieve a vector by tenant and vector ID.</summary>
    ValueTask<VectorRecord?> GetAsync(string tenantId, string vectorId, CancellationToken ct);

    /// <summary>Delete a vector by tenant and vector ID.</summary>
    ValueTask DeleteAsync(string tenantId, string vectorId, CancellationToken ct);
}

/// <summary>
/// Hybrid search combining vector similarity with keyword/filter matching.
/// </summary>
public interface IHybridSearchStore
{
    /// <summary>Execute a hybrid (vector + keyword) search.</summary>
    ValueTask<IReadOnlyList<VectorHit>> SearchAsync(HybridSearchQuery query, CancellationToken ct);
}

/// <summary>
/// Administrative operations for vector stores (collection management).
/// </summary>
public interface IVectorStoreAdministration
{
    /// <summary>Ensure a collection exists with the given spec, creating if needed.</summary>
    ValueTask EnsureCollectionAsync(VectorCollectionSpec spec, CancellationToken ct);

    /// <summary>Delete a collection.</summary>
    ValueTask DeleteCollectionAsync(string tenantId, string collectionName, CancellationToken ct);

    /// <summary>Get metadata for a collection.</summary>
    ValueTask<CollectionMeta?> GetCollectionMetaAsync(string tenantId, string collectionName, CancellationToken ct);
}

/// <summary>
/// Embedding plugin — generates vector embeddings from text inputs.
/// Must be independent from IVectorStore per architecture constraint.
/// </summary>
public interface IEmbeddingPlugin
{
    /// <summary>Generate embeddings for input texts using the specified model.</summary>
    ValueTask<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, string model, CancellationToken ct);
}

/// <summary>
/// Reranker plugin — reranks search results for improved relevance.
/// Must be independent from IVectorStore per architecture constraint.
/// </summary>
public interface IRerankerPlugin
{
    /// <summary>Rerank candidates against a query, returning top-K results.</summary>
    ValueTask<IReadOnlyList<RerankHit>> RerankAsync(string query, IReadOnlyList<RerankCandidate> candidates, int topK, CancellationToken ct);
}
