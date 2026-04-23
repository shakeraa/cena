// =============================================================================
// Cena Platform — Subscription management DTOs (EPIC-PRR-I, ADR-0057)
//
// Authenticated-parent-scoped management endpoints under /api/me/subscription.
// No PII in the wire format — the parent is identified by the session claim,
// the primary student by the parent's first consent-linked child.
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>Body of POST /api/me/subscription/activate.</summary>
public sealed record ActivateSubscriptionRequest(
    string PrimaryStudentId,
    string Tier,            // "Basic" | "Plus" | "Premium"
    string BillingCycle,    // "Monthly" | "Annual"
    string PaymentIdempotencyKey);

/// <summary>Summary of the current subscription state returned to the caller.</summary>
public sealed record SubscriptionStatusDto(
    string Status,                    // "Unsubscribed" | "Active" | "PastDue" | "Cancelled" | "Refunded"
    string? CurrentTier,              // null when Unsubscribed
    string? CurrentBillingCycle,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? RenewsAt,
    int LinkedStudentCount);

/// <summary>Body of POST /api/me/subscription/siblings.</summary>
public sealed record LinkSiblingRequest(
    string SiblingStudentId,
    string Tier);   // sibling may be on a different tier than primary (persona #7)

/// <summary>Body of PATCH /api/me/subscription/tier.</summary>
public sealed record ChangeTierRequest(string NewTier);

/// <summary>Body of PATCH /api/me/subscription/cycle.</summary>
public sealed record ChangeCycleRequest(string NewCycle);

/// <summary>Body of POST /api/me/subscription/refund.</summary>
public sealed record RefundRequest(string Reason);

/// <summary>Body of POST /api/me/subscription/cancel. PRR-331: the optional
/// <paramref name="ChurnReasonCategory"/> + <paramref name="ChurnFreeText"/>
/// capture the structured survey response (dropdown + free text). Both are
/// optional — a cancel without a survey still works, but the dashboard
/// gets less signal.</summary>
public sealed record CancelRequest(
    string Reason,
    string? ChurnReasonCategory = null,
    string? ChurnFreeText = null);

/// <summary>
/// Response for GET /api/me/subscription/guarantee-window (PRR-294).
/// Drives the "Request refund" CTA on the parent's account → billing screen
/// and the "N days left" copy in lifecycle emails.
/// </summary>
/// <param name="IsWithinWindow">True iff the CTA should be surfaced today.</param>
/// <param name="DaysRemaining">Whole days remaining, ceiling-rounded. Zero outside the window.</param>
/// <param name="WindowEndsAtUtc">Absolute UTC instant the window closes. Null when never activated.</param>
/// <param name="Reason">Stable machine-readable reason code. See <c>MoneyBackGuaranteeWindowReason</c>.</param>
public sealed record GuaranteeWindowStatusDto(
    bool IsWithinWindow,
    int DaysRemaining,
    DateTimeOffset? WindowEndsAtUtc,
    string Reason);
