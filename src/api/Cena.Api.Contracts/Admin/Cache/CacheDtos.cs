// =============================================================================
// Cena Platform -- Explanation Cache Admin DTOs (ADM-018)
// =============================================================================

namespace Cena.Api.Contracts.Admin.Cache;

// ── Cache Statistics ──

public sealed record ExplanationCacheStatsResponse(
    int L1Count,
    int L2KeyCount,
    long L2MemoryBytes,
    int L3GenerationCount,
    float OverallHitRate,
    float L1HitRate,
    float L2HitRate,
    float L3HitRate);

// ── Explanations by Question ──

public sealed record ExplanationsByQuestionResponse(
    string QuestionId,
    string QuestionStem,
    IReadOnlyList<ExplanationVersionDto> Explanations);

public sealed record ExplanationVersionDto(
    string Level,
    string Language,
    string Text,
    string ErrorType,
    DateTimeOffset CreatedAt,
    QualityScoreDto? QualityScores);

public sealed record QualityScoreDto(
    float Factual,
    float Linguistic,
    float Pedagogical,
    float Composite);

// ── Invalidation ──

/// <summary>
/// Invalidate cached explanations. Level is "L1", "L2", "L3", or "all".
/// </summary>
public sealed record InvalidateCacheRequest(
    string QuestionId,
    string Level);

public sealed record InvalidateCacheResponse(
    int InvalidatedCount);

// ── Quality Scores ──

public sealed record ExplanationQualityListResponse(
    IReadOnlyList<ExplanationQualityItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ExplanationQualityItemDto(
    string QuestionId,
    string StemPreview,
    float Factual,
    float Linguistic,
    float Pedagogical,
    float Composite,
    string Language,
    DateTimeOffset CreatedAt);
