// =============================================================================
// Cena Platform -- Cultural Context DTOs
// ADM-012: Cultural equity and inclusion monitoring
// =============================================================================

namespace Cena.Api.Contracts.Admin.Cultural;

// Cultural Distribution Dashboard
public sealed record CulturalDistributionResponse(
    IReadOnlyList<CulturalGroup> Groups,
    IReadOnlyList<ResilienceComparison> ResilienceByGroup,
    IReadOnlyList<MethodologyByCulture> MethodologyEffectiveness,
    IReadOnlyList<FocusPatternByCulture> FocusPatterns);

public sealed record CulturalGroup(
    string Context,  // HebrewDominant, ArabicDominant, Bilingual, Unknown
    int StudentCount,
    float Percentage);

public sealed record ResilienceComparison(
    string CulturalContext,
    float AvgResilienceScore,
    float P25,
    float P50,
    float P75,
    float P95);

public sealed record MethodologyByCulture(
    string Methodology,
    IReadOnlyList<CultureSuccessRate> ByCulture);

public sealed record CultureSuccessRate(
    string CulturalContext,
    float SuccessRate,
    int SampleSize);

public sealed record FocusPatternByCulture(
    string CulturalContext,
    float AvgSessionDuration,
    float AvgFocusScore,
    float MicrobreakAcceptance,
    string PeakFocusTime);

// Equity Alerts
public sealed record EquityAlertsResponse(
    IReadOnlyList<EquityAlert> ActiveAlerts,
    IReadOnlyList<ContentBalanceRecommendation> Recommendations);

public sealed record EquityAlert(
    string Id,
    string Severity,  // info, warning, critical
    string Type,  // mastery_gap, content_imbalance, methodology_ineffective
    string Description,
    string CulturalContext,
    float DeviationPercent,
    DateTimeOffset DetectedAt);

public sealed record ContentBalanceRecommendation(
    string Language,
    string Subject,
    int CurrentCount,
    int RecommendedCount,
    string GapDescription);

// ─────────────────────────────────────────────────────────────────────────────
// prr-034: Cultural Context Community Review Board — ops queue DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Enqueue request for a new cultural-context DLQ entry. The enqueue path
/// is not directly student-exposed — callers are moderation scanners,
/// human moderators, and triage pipelines.
/// </summary>
public sealed record CulturalContextEnqueueRequest(
    string SchoolId,
    string SubjectKind,        // "question" | "explanation" | "hint" | "bagrut-recreation"
    string SubjectId,
    string ConcernCategory,    // "language-register" | "cultural-reference-clarity" | ...
    string Reason,
    string Source,             // "scanner-miss" | "moderator-flagged" | "student-reported"
    string? CorrelationId,
    string? EnqueuedByOperatorId);

/// <summary>
/// Ops-queue listing DTO. Single entry.
/// </summary>
public sealed record CulturalContextDlqItem(
    string Id,
    string SchoolId,
    string SubjectKind,
    string SubjectId,
    string ConcernCategory,
    string Reason,
    string Source,
    string? CorrelationId,
    string? EnqueuedByOperatorId,
    DateTimeOffset EnqueuedAt,
    string Status,
    string? AssignedReviewerId,
    DateTimeOffset? DecidedAt,
    string? DecisionOutcome);

public sealed record CulturalContextDlqListResponse(
    IReadOnlyList<CulturalContextDlqItem> Items,
    int Page,
    int PageSize,
    int Total);
