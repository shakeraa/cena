// =============================================================================
// Cena Platform -- Content Vector Store Interface
// SAI-008: Abstraction over pgvector storage for content embeddings.
// Supports upsert, top-K search with metadata filters, and bulk operations.
// =============================================================================

namespace Cena.Actors.Embeddings;

/// <summary>
/// Metadata associated with a stored content embedding.
/// </summary>
public sealed record EmbeddingMetadata(
    string ContentBlockId,
    string? PipelineItemId,
    string ContentType,
    string Subject,
    string Topic,
    IReadOnlyList<string> ConceptIds,
    string Language,
    string TextPreview);

/// <summary>
/// A single search result from the vector store, including cosine similarity score.
/// </summary>
public sealed record VectorSearchResult(
    string ContentBlockId,
    string? PipelineItemId,
    string ContentType,
    string Subject,
    string Topic,
    IReadOnlyList<string> ConceptIds,
    string Language,
    string TextPreview,
    float Similarity);

/// <summary>
/// Filter criteria for vector search queries.
/// All filters are optional; null/empty means no filtering on that field.
/// </summary>
public sealed record VectorSearchFilter(
    string? Language = null,
    string? Subject = null,
    string? ContentType = null,
    IReadOnlyList<string>? ConceptIds = null);

/// <summary>
/// pgvector content store abstraction. Stores 384-dim embeddings with metadata
/// and supports top-K cosine similarity search with metadata filters.
/// Target: less than 50ms search for 10K items.
/// </summary>
public interface IContentVectorStore
{
    /// <summary>
    /// Upserts a single embedding with metadata. Uses ON CONFLICT DO UPDATE
    /// so repeated calls for the same content_block_id are idempotent.
    /// </summary>
    Task UpsertAsync(
        EmbeddingMetadata metadata,
        float[] embedding,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts multiple embeddings in a single database round-trip.
    /// </summary>
    Task UpsertBatchAsync(
        IReadOnlyList<(EmbeddingMetadata Metadata, float[] Embedding)> items,
        CancellationToken ct = default);

    /// <summary>
    /// Searches for the top-K most similar embeddings to the given query vector.
    /// Applies optional metadata filters (language, subject, content type, concept IDs).
    /// Returns results sorted by descending similarity.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        float minSimilarity = 0.7f,
        VectorSearchFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the embedding for the given content block ID.
    /// </summary>
    Task DeleteAsync(string contentBlockId, CancellationToken ct = default);
}
