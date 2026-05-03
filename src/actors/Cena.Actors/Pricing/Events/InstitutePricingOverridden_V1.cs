// =============================================================================
// Cena Platform — InstitutePricingOverridden_V1 event (prr-244)
//
// Append-only event emitted on every POST /api/admin/institutes/{id}/
// pricing-override. The event stream is the source of truth; the
// InstitutePricingOverrideDocument projection is derived.
//
// Carries old + new values so audit logs + SIEM feeds can render a diff
// without a separate query. Also carries TraceId so we can correlate
// with the SIEM tag `pricing.override.applied`.
// =============================================================================

namespace Cena.Actors.Pricing.Events;

/// <summary>
/// SUPER_ADMIN applied a pricing override to one institute. The event
/// body is the complete new pricing record plus the pre-change values
/// (defaults when no prior override existed) for diff rendering.
/// </summary>
/// <param name="InstituteId">Institute the override applies to.</param>
/// <param name="OldStudentMonthlyPriceUsd">Pre-change student monthly
/// price — either the prior override value or the YAML default.</param>
/// <param name="NewStudentMonthlyPriceUsd">Post-change value.</param>
/// <param name="OldInstitutionalPerSeatPriceUsd">Pre-change seat price.</param>
/// <param name="NewInstitutionalPerSeatPriceUsd">Post-change seat price.</param>
/// <param name="OldMinSeatsForInstitutional">Pre-change seat breakpoint.</param>
/// <param name="NewMinSeatsForInstitutional">Post-change seat breakpoint.</param>
/// <param name="OldFreeTierSessionCap">Pre-change free-tier cap.</param>
/// <param name="NewFreeTierSessionCap">Post-change free-tier cap.</param>
/// <param name="JustificationText">Required ≥20 char business rationale.</param>
/// <param name="EffectiveFromUtc">When the override starts applying.</param>
/// <param name="EffectiveUntilUtc">Optional end; null = open-ended.</param>
/// <param name="OverriddenBySuperAdminId">SUPER_ADMIN user id.</param>
/// <param name="TraceId">Correlates with the SIEM
/// <c>pricing.override.applied</c> tag for the same write.</param>
/// <param name="OccurredAtUtc">Event timestamp — wall clock at write.</param>
public sealed record InstitutePricingOverridden_V1(
    string InstituteId,
    decimal OldStudentMonthlyPriceUsd,
    decimal NewStudentMonthlyPriceUsd,
    decimal OldInstitutionalPerSeatPriceUsd,
    decimal NewInstitutionalPerSeatPriceUsd,
    int OldMinSeatsForInstitutional,
    int NewMinSeatsForInstitutional,
    int OldFreeTierSessionCap,
    int NewFreeTierSessionCap,
    string JustificationText,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveUntilUtc,
    string OverriddenBySuperAdminId,
    string TraceId,
    DateTimeOffset OccurredAtUtc);
