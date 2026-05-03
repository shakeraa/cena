// =============================================================================
// Cena Platform — DiscountRevoked_V1 (per-user discount-codes feature)
//
// Terminal event marking a discount assignment as administratively cancelled.
// Cannot be issued after a DiscountRedeemed_V1 — the service rejects revoke
// post-redemption with reason 'already_redeemed'.
//
// Side effects (NOT carried in the event payload, but performed by the
// admin endpoint before append):
//   - Stripe Coupon deletion via Stripe.CouponService.DeleteAsync
//   - Stripe Promotion Code deactivation via Stripe.PromotionCodeService.UpdateAsync(active=false)
//
// On revoke the discount no longer surfaces at /api/me/applicable-discount;
// student-side banner disappears on next page load.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Emitted when an admin revokes an active discount assignment.
/// </summary>
/// <param name="AssignmentId">Discount assignment id (stream key).</param>
/// <param name="TargetEmailNormalized">Canonical email the assignment was issued for.</param>
/// <param name="RevokedByAdminSubjectIdEncrypted">Encrypted admin subject id (audit).</param>
/// <param name="Reason">Free-text reason. Defaults to "admin_revoked".</param>
/// <param name="RevokedAt">UTC timestamp of revocation.</param>
public sealed record DiscountRevoked_V1(
    string AssignmentId,
    string TargetEmailNormalized,
    string RevokedByAdminSubjectIdEncrypted,
    string Reason,
    DateTimeOffset RevokedAt);
