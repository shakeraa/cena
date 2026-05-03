// =============================================================================
// Cena Platform — Entitlement DTOs (Phase 1D + 1D-fix, trial-then-paywall §11)
//
// Wire-format records for the consumer-facing entitlement surface:
//   GET  /api/me/entitlement   → EntitlementResponseDto
//   POST /api/me/start-trial   → StartTrialRequestDto / EntitlementResponseDto
//
// /api/me/redeem-code was removed in Phase 1D-fix because the codebase has
// no code-driven redemption registry — admin issues per-email and Stripe
// auto-binds at checkout. The SPA peeks discount availability via the
// pre-existing GET /api/me/applicable-discount.
//
// Banned-mechanics guard (ADR-0048, GD-004): no field name implies streak,
// shrinking-urgency, scarcity, or loss-aversion. `daysRemaining` and
// `endsAt` are factual datapoints (the user has the right to know what
// they bought); the SPA renders them as plain calendar info, never as a
// pressure-mounting timer.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>
/// Top-level entitlement view returned by GET /api/me/entitlement and as
/// the success body of POST /api/me/start-trial. Exactly one of
/// <see cref="Trial"/> / <see cref="Subscription"/> is non-null when the
/// student is entitled; both null when Unsubscribed/Expired.
/// </summary>
/// <param name="Tier">Catalog tier name — e.g. "TrialPlus", "Plus", "Premium", "Unsubscribed".</param>
/// <param name="EffectiveStatus">"Active" | "Trialing" | "PastDue" | "Unsubscribed" | "Expired" | "Cancelled" | "Refunded".</param>
/// <param name="HasPaymentMethodOnFile">
/// True iff the parent stream has a SetupIntent-attached card. Conversion
/// flow uses this to decide whether the SPA can skip the card-collection
/// step. The raw payment-method id is intentionally NOT surfaced.
/// </param>
/// <param name="Trial">Populated when <paramref name="EffectiveStatus"/> is "Trialing".</param>
/// <param name="Subscription">Populated when <paramref name="EffectiveStatus"/> is "Active" or "PastDue".</param>
/// <param name="DiscountApplied">When non-null, an applicable per-user discount is staged for the next checkout.</param>
public sealed record EntitlementResponseDto(
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("effectiveStatus")] string EffectiveStatus,
    [property: JsonPropertyName("hasPaymentMethodOnFile")] bool HasPaymentMethodOnFile,
    [property: JsonPropertyName("trial")] TrialStateDto? Trial,
    [property: JsonPropertyName("subscription")] SubscriptionStateDto? Subscription,
    [property: JsonPropertyName("discountApplied")] ApplicableDiscountDto? DiscountApplied);

/// <summary>
/// Trial-specific state. Caps come from the snapshot pinned at trial-start;
/// usage counters come from <c>IStudentTrialConsumptionStore</c>. Cap = 0
/// means "no per-trial cap on this feature" per <c>TrialAllotmentConfig</c>.
/// </summary>
public sealed record TrialStateDto(
    [property: JsonPropertyName("endsAt")] DateTimeOffset? EndsAt,
    [property: JsonPropertyName("daysRemaining")] int? DaysRemaining,
    [property: JsonPropertyName("tutorTurnsUsed")] int TutorTurnsUsed,
    [property: JsonPropertyName("tutorTurnsCap")] int TutorTurnsCap,
    [property: JsonPropertyName("photoDiagnosticsUsed")] int PhotoDiagnosticsUsed,
    [property: JsonPropertyName("photoDiagnosticsCap")] int PhotoDiagnosticsCap,
    [property: JsonPropertyName("sessionsStarted")] int SessionsStarted,
    [property: JsonPropertyName("sessionsCap")] int SessionsCap);

/// <summary>
/// Paid-subscription state. Renewal timestamp is factual; the SPA renders
/// as plain calendar info.
/// </summary>
public sealed record SubscriptionStateDto(
    [property: JsonPropertyName("renewsAt")] DateTimeOffset? RenewsAt,
    [property: JsonPropertyName("billingCycle")] string BillingCycle);

/// <summary>
/// Body of POST /api/me/start-trial. <see cref="TrialKind"/> chooses the
/// fingerprint-collection mode:
///   "SelfPay" / "ParentPay" — caller MUST supply <see cref="SetupIntentId"/>;
///                              server re-reads via Stripe and extracts the
///                              card.fingerprint (§5.14 server-side rule).
///   "InstituteCode"          — caller MUST supply <see cref="InstituteCode"/>;
///                              no card collected.
/// </summary>
public sealed record StartTrialRequestDto(
    [property: JsonPropertyName("trialKind")] string TrialKind,
    [property: JsonPropertyName("setupIntentId")] string? SetupIntentId,
    [property: JsonPropertyName("instituteCode")] string? InstituteCode,
    [property: JsonPropertyName("experimentVariantId")] string? ExperimentVariantId);
