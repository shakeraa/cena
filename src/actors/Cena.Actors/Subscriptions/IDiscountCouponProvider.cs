// =============================================================================
// Cena Platform — IDiscountCouponProvider (per-user discount-codes feature)
//
// Port for "create / revoke a per-user discount coupon at the payment
// gateway". Stripe is the canonical adapter; an in-memory adapter exists
// for dev/test composition.
//
// Lifecycle:
//   CreateCouponAsync     — admin issues discount → mint Stripe Coupon
//                           + Promotion Code restricted to the target email
//   RevokeCouponAsync     — admin revokes → delete Coupon + deactivate Promotion Code
//
// Distinct from ICheckoutSessionProvider because:
//   - they own different lifecycle slices (coupon vs session)
//   - swapping the coupon side without swapping the checkout side is a
//     legitimate composition (e.g. testing the discount aggregate against
//     real Stripe coupons but a sandbox checkout) — keeps the seams narrow
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Input to <see cref="IDiscountCouponProvider.CreateCouponAsync"/>.
/// </summary>
/// <param name="AssignmentId">
/// Cena-side assignment id. Embedded in the gateway coupon's metadata so
/// the webhook handler can reconstruct the assignment without a secondary
/// lookup.
/// </param>
/// <param name="TargetEmailNormalized">
/// Lower-cased, Gmail-folded canonical email. Stored as gateway metadata;
/// also used to restrict the Promotion Code to a Stripe Customer if one
/// already exists for the email (otherwise the promo code is restricted-
/// only-by-metadata and Cena binds at first-checkout-customer-creation).
/// </param>
/// <param name="DiscountKind">PercentOff or AmountOff.</param>
/// <param name="DiscountValue">
/// Basis points (PercentOff) or agorot (AmountOff). The provider converts
/// to whatever units the gateway requires.
/// </param>
/// <param name="DurationMonths">
/// Number of paid invoices the discount applies to (1..36). Stripe uses
/// <c>duration: repeating</c> with <c>duration_in_months: N</c>.
/// </param>
public sealed record CouponCreateRequest(
    string AssignmentId,
    string TargetEmailNormalized,
    DiscountKind DiscountKind,
    int DiscountValue,
    int DurationMonths);

/// <summary>Response from <see cref="IDiscountCouponProvider.CreateCouponAsync"/>.</summary>
/// <param name="CouponId">Gateway coupon id (Stripe: cou_…).</param>
/// <param name="PromotionCodeId">
/// Gateway promotion-code id (Stripe: promo_…). The internal id is
/// distinct from the human-readable <see cref="PromotionCodeString"/>.
/// </param>
/// <param name="PromotionCodeString">
/// Human-readable code string the admin can give to the user out-of-band
/// (e.g. as a fallback to the auto-applied flow). Stripe auto-generates
/// when not specified; the in-memory adapter uses a deterministic prefix.
/// </param>
public sealed record CouponCreateResult(
    string CouponId,
    string PromotionCodeId,
    string PromotionCodeString);

/// <summary>
/// Input to <see cref="IDiscountCouponProvider.RevokeCouponAsync"/>.
/// Caller passes the gateway-side ids captured at creation; the provider
/// drives the gateway-specific revoke flow.
/// </summary>
/// <param name="AssignmentId">Cena-side assignment id (logging / audit).</param>
/// <param name="CouponId">Gateway coupon id (Stripe: cou_…).</param>
/// <param name="PromotionCodeId">Gateway promotion-code id (Stripe: promo_…).</param>
public sealed record CouponRevokeRequest(
    string AssignmentId,
    string CouponId,
    string PromotionCodeId);

/// <summary>Port for creating + revoking per-user discount coupons.</summary>
public interface IDiscountCouponProvider
{
    /// <summary>Adapter name for metrics/audit ("stripe" | "in-memory" | ...).</summary>
    string Name { get; }

    /// <summary>
    /// Create a coupon and the customer-facing promotion code at the gateway.
    /// Returns gateway-side ids that Cena persists on the
    /// <c>DiscountIssued_V1</c> event.
    /// </summary>
    Task<CouponCreateResult> CreateCouponAsync(CouponCreateRequest request, CancellationToken ct);

    /// <summary>
    /// Revoke a previously-created coupon. Implementations MUST be idempotent
    /// — calling revoke twice for the same ids returns silently rather than
    /// surfacing a gateway 404.
    /// </summary>
    Task RevokeCouponAsync(CouponRevokeRequest request, CancellationToken ct);
}
