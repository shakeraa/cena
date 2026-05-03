// =============================================================================
// Cena Platform -- Cultural Context Documents (ADM-012)
// Marten-backed domain model for cultural equity monitoring.
// Replaces the Phase 1 stub in CulturalContextService with real docs.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// A rollup of student population by cultural context (language dominance).
/// One document per (schoolId, context) combination. Updated by the
/// CulturalContextRollupService as students complete onboarding.
/// </summary>
public class CulturalContextGroupDocument
{
    public string Id { get; set; } = "";          // "{schoolId}:{context}"
    public string SchoolId { get; set; } = "";
    public string Context { get; set; } = "";     // HebrewDominant | ArabicDominant | Bilingual | Unknown
    public int StudentCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Per-group resilience percentiles computed from flow-state events.</summary>
    public float AvgResilienceScore { get; set; }
    public float P25 { get; set; }
    public float P50 { get; set; }
    public float P75 { get; set; }
    public float P95 { get; set; }

    /// <summary>Per-group focus pattern aggregates computed from learning session events.</summary>
    public float AvgSessionMinutes { get; set; }
    public float AvgFocusScore { get; set; }
    public float MicrobreakAcceptance { get; set; }
    public string PeakFocusTime { get; set; } = "10:00-12:00";
}

/// <summary>
/// Effectiveness of a pedagogical methodology for a cultural context.
/// One row per (schoolId, methodology, context). Updated as methodology
/// outcomes are recorded.
/// </summary>
public class MethodologyEffectivenessByCultureDocument
{
    public string Id { get; set; } = "";             // "{schoolId}:{methodology}:{context}"
    public string SchoolId { get; set; } = "";
    public string Methodology { get; set; } = "";    // Socratic | WorkedExample | Feynman | RetrievalPractice
    public string CulturalContext { get; set; } = "";
    public float SuccessRate { get; set; }           // 0..1
    public int SampleSize { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An equity alert surfaced by the monitoring system. Created when a
/// cultural group's outcomes diverge from the school mean by more than
/// a configured threshold.
/// </summary>
public class EquityAlertDocument
{
    public string Id { get; set; } = "";          // alert-{hash}
    public string SchoolId { get; set; } = "";
    public string Severity { get; set; } = "info"; // info | warning | critical
    public string Type { get; set; } = "";        // mastery_gap | content_imbalance | methodology_ineffective
    public string Description { get; set; } = "";
    public string CulturalContext { get; set; } = "";
    public float DeviationPercent { get; set; }
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Acknowledged { get; set; } = false;
    public DateTimeOffset? AcknowledgedAt { get; set; }
}

/// <summary>
/// A recommendation for content balance between languages/cultures.
/// One row per (schoolId, language, subject). Computed by comparing the
/// question count per language.
/// </summary>
public class ContentBalanceRecommendationDocument
{
    public string Id { get; set; } = "";             // "{schoolId}:{language}:{subject}"
    public string SchoolId { get; set; } = "";
    public string Language { get; set; } = "";
    public string Subject { get; set; } = "";
    public int CurrentCount { get; set; }
    public int RecommendedCount { get; set; }
    public string GapDescription { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
