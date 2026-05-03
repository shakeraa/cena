// =============================================================================
// Cena Platform — ConsentAggregate (prr-155, EPIC-PRR-A)
//
// ConsentPurpose: the processing purposes governed by the aggregate.
//
// This is the aggregate-level purpose taxonomy (distinct from the legacy
// Cena.Infrastructure.Compliance.ProcessingPurpose, which is preserved
// verbatim for API-contract compatibility — see GdprConsentManager facade).
// The two enums map bidirectionally in ConsentPurposeMapping.
//
// Purposes are extensible: adding a new one requires an age-band-matrix
// review (AgeBandAuthorizationRules) and a PII classification review per
// ADR-0038 §"Field classification policy".
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Processing purposes governed by <see cref="ConsentAggregate"/>. Distinct
/// from the legacy <c>Cena.Infrastructure.Compliance.ProcessingPurpose</c>
/// enum; a bidirectional mapping preserves the existing API contract while
/// the aggregate-internal taxonomy evolves.
/// </summary>
public enum ConsentPurpose
{
    /// <summary>Detection of misconception patterns from student answers.</summary>
    MisconceptionDetection,

    /// <summary>Weekly / periodic parent digest emails and notifications.</summary>
    ParentDigest,

    /// <summary>Sharing student progress data with the assigned teacher.</summary>
    TeacherShare,

    /// <summary>Cross-subject aggregate analytics for institutional dashboards.</summary>
    AnalyticsAggregation,

    /// <summary>Third-party AI assistance (Anthropic Claude etc.) for tutoring.</summary>
    AiAssistance,

    /// <summary>External integrations (SIS sync, calendar feeds, LMS handoff).</summary>
    ExternalIntegration,

    /// <summary>Promotional / marketing notifications and feature nudges.</summary>
    MarketingNudges,

    /// <summary>Leaderboards and peer-visible progress displays.</summary>
    LeaderboardDisplay,

    /// <summary>Cross-school benchmarking in aggregate.</summary>
    CrossTenantBenchmarking
}
