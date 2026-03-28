// =============================================================================
// Cena Platform — Embedding Service
// SAI-07: Stores and searches content embeddings for RAG-based tutoring.
// Stub implementation: uses Marten full-text search as fallback until pgvector
// is configured. Real vector search will replace the keyword-based approach.
// =============================================================================

using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);

    Task StoreEmbeddingAsync(
        string contentId,
        float[] embedding,
        string subject,
        string? conceptId,
        string contentType,
        string language,
        string textPreview,
        CancellationToken ct);

    Task<IReadOnlyList<ContentMatch>> SearchAsync(EmbeddingQuery query, CancellationToken ct);
}

public sealed record EmbeddingQuery(
    string QueryText,
    string? SubjectFilter,
    string? ConceptIdFilter,
    string? LanguageFilter,
    int TopK = 5,
    float MinSimilarity = 0.7f);

public sealed record ContentMatch(
    string ContentId,
    string Text,
    string ContentType,
    float Similarity);

/// <summary>
/// Stub implementation that uses Marten document queries instead of pgvector.
/// Generates a deterministic hash-based pseudo-embedding for interface compatibility.
/// Search falls back to keyword matching against ContentDocument text fields.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<EmbeddingService> _logger;

    private const int EmbeddingDimension = 384;

    public EmbeddingService(IDocumentStore store, ILogger<EmbeddingService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Generates a deterministic pseudo-embedding from text using hash bucketing.
    /// This is a placeholder — will be replaced with a real embedding model call.
    /// </summary>
    public Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var embedding = new float[EmbeddingDimension];
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(embedding);

        // Hash-based pseudo-embedding: deterministic, zero external calls
        var bytes = System.Text.Encoding.UTF8.GetBytes(text.ToLowerInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);

        for (var i = 0; i < EmbeddingDimension; i++)
        {
            var byteIndex = i % hash.Length;
            embedding[i] = (hash[byteIndex] / 255f) * 2f - 1f; // Normalize to [-1, 1]
        }

        // Normalize to unit vector
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (var i = 0; i < embedding.Length; i++)
                embedding[i] /= magnitude;
        }

        return Task.FromResult(embedding);
    }

    /// <summary>
    /// Stores embedding metadata. In the stub implementation this is a no-op because
    /// search queries ContentDocument directly. When pgvector is enabled, this will
    /// INSERT into a dedicated embeddings table.
    /// </summary>
    public Task StoreEmbeddingAsync(
        string contentId,
        float[] embedding,
        string subject,
        string? conceptId,
        string contentType,
        string language,
        string textPreview,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Embedding stored (stub) for content {ContentId}, subject={Subject}, type={ContentType}",
            contentId, subject, contentType);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keyword-based search fallback. Queries ContentDocument via Marten,
    /// filtering by subject/language/concept and matching on text content.
    /// Returns results ranked by ContentType relevance and confidence.
    /// </summary>
    public async Task<IReadOnlyList<ContentMatch>> SearchAsync(EmbeddingQuery query, CancellationToken ct)
    {
        await using var session = _store.QuerySession();

        var queryable = session.Query<ContentDocument>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SubjectFilter))
            queryable = queryable.Where(d => d.Subject == query.SubjectFilter);

        if (!string.IsNullOrWhiteSpace(query.LanguageFilter))
            queryable = queryable.Where(d => d.Language == query.LanguageFilter);

        if (!string.IsNullOrWhiteSpace(query.ConceptIdFilter))
            queryable = queryable.Where(d => d.AssociatedConceptId == query.ConceptIdFilter);

        // Keyword matching: split query into words, match against text
        var keywords = query.QueryText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2)
            .Take(10)
            .ToList();

        var candidates = await queryable
            .OrderByDescending(d => d.Confidence)
            .Take(query.TopK * 3) // Over-fetch then re-rank
            .ToListAsync(ct);

        // Re-rank by keyword overlap (simple TF scoring)
        var scored = candidates
            .Select(doc =>
            {
                var textLower = doc.Text.ToLowerInvariant();
                var hits = keywords.Count(kw => textLower.Contains(kw.ToLowerInvariant()));
                var similarity = keywords.Count > 0
                    ? (float)hits / keywords.Count
                    : doc.Confidence;
                return (Doc: doc, Similarity: similarity);
            })
            .Where(x => x.Similarity >= query.MinSimilarity || keywords.Count == 0)
            .OrderByDescending(x => x.Similarity)
            .Take(query.TopK)
            .Select(x => new ContentMatch(
                ContentId: x.Doc.Id,
                Text: x.Doc.Text,
                ContentType: x.Doc.Type.ToString(),
                Similarity: x.Similarity))
            .ToList();

        _logger.LogDebug(
            "Embedding search (stub) for '{Query}': {Count} results from {Candidates} candidates",
            Truncate(query.QueryText, 60), scored.Count, candidates.Count);

        return scored;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "...");
}
