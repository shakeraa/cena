// =============================================================================
// Cena Platform -- Embedding Admin DTOs (ADM-020)
// =============================================================================

namespace Cena.Api.Contracts.Admin.Embeddings;

// ── Corpus Statistics ──

public sealed record EmbeddingCorpusStatsResponse(
    int TotalBlocks,
    Dictionary<string, int> SubjectCounts,
    Dictionary<string, int> ConceptCounts,
    Dictionary<string, int> ContentTypeCounts,
    Dictionary<string, int> LanguageCounts,
    float IndexSizeMb);

// ── Search ──

public sealed record EmbeddingSearchRequest(
    string Query,
    int TopK,
    string? SubjectFilter,
    string? ConceptFilter,
    float? SimilarityThreshold);

public sealed record EmbeddingSearchResponse(
    IReadOnlyList<EmbeddingSearchResultDto> Results,
    long QueryTimeMs);

public sealed record EmbeddingSearchResultDto(
    string ContentBlockId,
    string TextPreview,
    string Subject,
    string ContentType,
    IReadOnlyList<string> ConceptIds,
    float Similarity,
    string Language);

// ── Duplicate Detection ──

public sealed record EmbeddingDuplicateResponse(
    IReadOnlyList<DuplicatePairDto> Duplicates,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record DuplicatePairDto(
    string Block1Id,
    string Block1Preview,
    string Block2Id,
    string Block2Preview,
    float Similarity,
    string Subject);

// ── Reindex ──

/// <summary>
/// Request a corpus reindex. Scope: "all", "subject", or "concept".
/// Filter narrows scope (e.g. subject name or concept ID).
/// </summary>
public sealed record ReindexRequest(
    string Scope,
    string? Filter);

public sealed record ReindexResponse(
    string JobId,
    int EstimatedBlocks);
