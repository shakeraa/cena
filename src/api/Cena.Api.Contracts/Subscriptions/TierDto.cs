// =============================================================================
// Cena Platform — Subscription DTOs (EPIC-PRR-I PRR-290/291, ADR-0057)
//
// Wire contracts for the pricing catalog. Intentionally separate from the
// domain TierDefinition so the domain can evolve internally without a
// breaking wire change. The catalog response is PUBLIC (anonymous GET OK)
// so no PII leaks through.
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// Usage caps as exposed on the wire. -1 sentinel surfaces as the string
/// "unlimited" in the JSON to make frontend rendering explicit (and to stop
/// any UI from rendering the literal number -1 if the sentinel ever leaks).
/// </summary>
/// <param name="SonnetEscalationsPerWeek">Null when unlimited.</param>
/// <param name="PhotoDiagnosticsPerMonth">Null when unlimited.</param>
/// <param name="PhotoDiagnosticsHardCapPerMonth">Null when unlimited.</param>
/// <param name="HintRequestsPerMonth">Null when unlimited.</param>
public sealed record UsageCapsDto(
    int? SonnetEscalationsPerWeek,
    int? PhotoDiagnosticsPerMonth,
    int? PhotoDiagnosticsHardCapPerMonth,
    int? HintRequestsPerMonth);

/// <summary>Feature flags exposed to the pricing UI.</summary>
public sealed record TierFeatureFlagsDto(
    bool ParentDashboard,
    bool TutorHandoffPdf,
    bool ArabicDashboard,
    bool PrioritySupport);

/// <summary>
/// A single retail tier on the pricing card. <see cref="TierId"/> is the
/// string form of <c>SubscriptionTier</c> (stable contract). Prices are
/// VAT-inclusive integer agorot; the frontend formats the display.
/// </summary>
public sealed record RetailTierDto(
    string TierId,
    long MonthlyPriceAgorot,
    long AnnualPriceAgorot,
    long MonthlyVatAgorot,
    long AnnualVatAgorot,
    UsageCapsDto Caps,
    TierFeatureFlagsDto Features);

/// <summary>Sibling discount structure on the pricing page.</summary>
public sealed record SiblingDiscountDto(
    long FirstSecondSiblingMonthlyAgorot,
    long ThirdPlusSiblingMonthlyAgorot);

/// <summary>
/// Response for <c>GET /api/v1/tiers</c>. Retail tiers in display order
/// (Basic, Plus, Premium) plus the sibling-discount structure.
/// </summary>
public sealed record PricingCatalogResponseDto(
    IReadOnlyList<RetailTierDto> Tiers,
    SiblingDiscountDto SiblingDiscount,
    int VatBasisPoints);
