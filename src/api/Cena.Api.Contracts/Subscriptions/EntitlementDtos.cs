// =============================================================================
// Cena Platform — Entitlement DTOs (Phase 1D, trial-then-paywall §11)
//
// Wire-format records for the consumer-facing entitlement surface:
//   GET    /api/me/entitlement       → EntitlementResponseDto
//   POST   /api/me/start-trial       → StartTrialRequestDto / EntitlementResponseDto
//   POST   /api/me/redeem-code       → RedeemCodeRequestDto / RedeemCodeResponseDto
//
// Banned-mechanics guard (ADR-0048, GD-004): no field name implies streak,
// countdown urgency, scarcity, or loss-aversion. `daysRemaining` and
// `endsAt` are factual datapoints (the user has the right to know what
// they bought); the SPA renders them as plain calendar info, never as a
// shrinking-urgency timer.
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
/// <param name="Trial">Populated when <paramref name="EffectiveStatus"/> is "Trialing".</param>
/// <param name="Subscription">Populated when <paramref name="EffectiveStatus"/> is "Active" or "PastDue".</param>
/// <param name="DiscountApplied">When non-null, an applicable per-user discount is staged for the next checkout.</param>
public sealed record EntitlementResponseDto(
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("effectiveStatus")] string EffectiveStatus,
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
///   "InstituteCode"          — no card collected; institute code authorises;
///                              <see cref="SetupIntentId"/> is ignored.
/// </summary>
public sealed record StartTrialRequestDto(
    [property: JsonPropertyName("trialKind")] string TrialKind,
    [property: JsonPropertyName("setupIntentId")] string? SetupIntentId,
    [property: JsonPropertyName("instituteCode")] string? InstituteCode,
    [property: JsonPropertyName("experimentVariantId")] string? ExperimentVariantId);

/// <summary>Body of POST /api/me/redeem-code (peek for an applicable discount).</summary>
public sealed record RedeemCodeRequestDto(
    [property: JsonPropertyName("code")] string Code);

/// <summary>
/// Response from POST /api/me/redeem-code. <see cref="Applied"/> is true
/// when an active discount was found for the caller's email; the SPA
/// stages it for the next checkout. The discount itself is applied at
/// Stripe-checkout time via the existing promotion-code passthrough.
/// </summary>
public sealed record RedeemCodeResponseDto(
    [property: JsonPropertyName("applied")] bool Applied,
    [property: JsonPropertyName("discountKind")] string? DiscountKind,
    [property: JsonPropertyName("discountValue")] int? DiscountValue,
    [property: JsonPropertyName("durationMonths")] int? DurationMonths,
    [property: JsonPropertyName("reason")] string? Reason);
