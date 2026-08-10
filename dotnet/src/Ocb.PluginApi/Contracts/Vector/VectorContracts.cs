using Ocb.Contracts;

namespace Ocb.PluginApi.Vector;

/// <summary>
/// Vector record stored in the vector database.
/// </summary>
public sealed record VectorRecord(
    string TenantId,
    string VectorId,
    string Model,
    int Dimension,
    float[] Embedding,
    IReadOnlyDictionary<string, string>? Payload
);

/// <summary>
/// Query for vector similarity search.
/// </summary>
public sealed record VectorQuery(
    string TenantId,
    string Model,
    int Dimension,
    float[] QueryEmbedding,
    int TopK,
    IReadOnlyDictionary<string, string>? Filter = null
);

/// <summary>
/// Query for hybrid (vector + keyword) search.
/// </summary>
public sealed record HybridSearchQuery(
    string TenantId,
    string Model,
    int Dimension,
    float[] QueryEmbedding,
    string? KeywordQuery,
    int TopK,
    float VectorWeight = 0.7f,
    IReadOnlyDictionary<string, string>? Filter = null
);

/// <summary>
/// Result hit from vector or hybrid search.
/// </summary>
public sealed record VectorHit(
    string VectorId,
    float Score,
    IReadOnlyDictionary<string, string>? Payload
);

/// <summary>
/// Candidate for reranking.
/// </summary>
public sealed record RerankCandidate(
    string VectorId,
    string? Content,
    float InitialScore,
    IReadOnlyDictionary<string, string>? Payload
);

/// <summary>
/// Result from reranking.
/// </summary>
public sealed record RerankHit(
    string VectorId,
    string? Content,
    float RerankScore,
    int Rank
);

/// <summary>
/// Specification for a vector collection.
/// </summary>
public sealed record VectorCollectionSpec(
    string TenantId,
    string CollectionName,
    int Dimension,
    string Distance = "cosine"
);

/// <summary>
/// Metadata describing an existing vector collection.
/// </summary>
public sealed record CollectionMeta(
    string Model,
    int Dimension,
    string Distance = "cosine",
    long VectorCount = 0
);

/// <summary>
/// Thrown when model or dimension compatibility checks fail.
/// </summary>
public sealed class VectorCompatibilityException : Exception
{
    public VectorCompatibilityException(string message) : base(message) { }
    public VectorCompatibilityException(string message, Exception inner) : base(message, inner) { }
}
