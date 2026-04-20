// =============================================================================
// Cena Platform — ConsentAggregate purpose mapping (prr-155)
//
// Bidirectional mapping between the aggregate-internal ConsentPurpose and
// the legacy Cena.Infrastructure.Compliance.ProcessingPurpose preserved for
// API-contract compatibility. The two enums have different extensibility
// paths (ConsentPurpose evolves with aggregate requirements;
// ProcessingPurpose is frozen by the existing public API) so a mapping
// table is the correct seam.
//
// The legacy "AccountAuth" and "SessionContinuity" purposes are always-
// required contract-necessity purposes that are NOT in the aggregate
// (they are not subject to per-subject consent decisions — they are
// lawful-basis: Contract). Mapping from those legacy values to a
// ConsentPurpose returns null.
// =============================================================================

using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Consent;

/// <summary>
/// Maps between legacy <see cref="ProcessingPurpose"/> and aggregate
/// <see cref="ConsentPurpose"/>. Used by the <c>GdprConsentManager</c>
/// facade to preserve its DTO shape while routing writes through the
/// new aggregate primitive.
/// </summary>
public static class ConsentPurposeMapping
{
    /// <summary>
    /// Map legacy purpose to aggregate purpose. Returns <c>null</c> for
    /// always-required contract-necessity purposes (AccountAuth,
    /// SessionContinuity) that are not consent-governed.
    /// </summary>
    public static ConsentPurpose? TryToConsentPurpose(ProcessingPurpose legacy) => legacy switch
    {
        ProcessingPurpose.AccountAuth => null,
        ProcessingPurpose.SessionContinuity => null,
        ProcessingPurpose.AdaptiveRecommendation => ConsentPurpose.MisconceptionDetection,
        ProcessingPurpose.PeerComparison => ConsentPurpose.LeaderboardDisplay,
        ProcessingPurpose.LeaderboardDisplay => ConsentPurpose.LeaderboardDisplay,
        ProcessingPurpose.SocialFeatures => ConsentPurpose.LeaderboardDisplay,
        ProcessingPurpose.ThirdPartyAi => ConsentPurpose.AiAssistance,
        ProcessingPurpose.BehavioralAnalytics => ConsentPurpose.AnalyticsAggregation,
        ProcessingPurpose.CrossTenantBenchmarking => ConsentPurpose.CrossTenantBenchmarking,
        ProcessingPurpose.MarketingNudges => ConsentPurpose.MarketingNudges,
        _ => null,
    };

    /// <summary>Map aggregate purpose to legacy purpose (for read-facade output).</summary>
    public static ProcessingPurpose? TryToLegacyPurpose(ConsentPurpose aggregate) => aggregate switch
    {
        ConsentPurpose.MisconceptionDetection => ProcessingPurpose.AdaptiveRecommendation,
        ConsentPurpose.LeaderboardDisplay => ProcessingPurpose.LeaderboardDisplay,
        ConsentPurpose.AiAssistance => ProcessingPurpose.ThirdPartyAi,
        ConsentPurpose.AnalyticsAggregation => ProcessingPurpose.BehavioralAnalytics,
        ConsentPurpose.CrossTenantBenchmarking => ProcessingPurpose.CrossTenantBenchmarking,
        ConsentPurpose.MarketingNudges => ProcessingPurpose.MarketingNudges,
        // ParentDigest, TeacherShare, ExternalIntegration — aggregate-only,
        // no legacy ProcessingPurpose equivalent. Caller must use
        // ConsentAggregate directly for these.
        ConsentPurpose.ParentDigest => null,
        ConsentPurpose.TeacherShare => null,
        ConsentPurpose.ExternalIntegration => null,
        _ => null,
    };
}
