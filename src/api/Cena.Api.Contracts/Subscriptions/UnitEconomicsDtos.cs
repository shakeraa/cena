// =============================================================================
// Cena Platform — Unit-economics DTOs (EPIC-PRR-I PRR-330)
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>Per-tier row on the admin unit-economics dashboard.</summary>
public sealed record TierEconomicsRowDto(
    string TierId,
    int ActiveSubscriptions,
    int PastDueSubscriptions,
    int CancelledInWindow,
    int RefundedInWindow,
    long RevenueAgorot,
    long RefundsAgorot,
    long NetRevenueAgorot);

/// <summary>Full unit-economics response for a window.</summary>
public sealed record UnitEconomicsResponseDto(
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<TierEconomicsRowDto> Rows,
    int TotalActive,
    long TotalRevenueAgorot,
    long TotalRefundsAgorot,
    long TotalNetRevenueAgorot);
