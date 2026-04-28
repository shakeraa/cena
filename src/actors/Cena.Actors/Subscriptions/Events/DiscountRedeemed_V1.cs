// =============================================================================
// Cena Platform — DiscountRedeemed_V1 (per-user discount-codes feature)
//
// Terminal event marking a discount assignment as consumed. Emitted when the
// student successfully completes a Stripe Checkout that attached the
// promotion code (Stripe carries the metadata back via the Checkout
// completed webhook). After redemption the discount is locked: no further
// issue/revoke operations against the same assignment id.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Emitted when the discount has been applied to a Stripe subscription
/// (at first paid invoice; Stripe handles the per-invoice continuation
/// up to <c>duration_in_months</c> natively).
/// </summary>
/// <param name="AssignmentId">Discount assignment id (stream key).</param>
/// <param name="TargetEmailNormalized">Canonical email this assignment was issued for.</param>
/// <param name="ParentSubjectIdEncrypted">
/// Encrypted parent subject id at the moment of redemption — links the
/// discount stream to the subscription stream for cross-context audit.
/// </param>
/// <param name="StripeSubscriptionId">
/// External Stripe subscription id where the discount was applied. Empty
/// for non-Stripe (in-memory) test composition.
/// </param>
/// <param name="RedeemedAt">UTC timestamp of redemption.</param>
public sealed record DiscountRedeemed_V1(
    string AssignmentId,
    string TargetEmailNormalized,
    string ParentSubjectIdEncrypted,
    string StripeSubscriptionId,
    DateTimeOffset RedeemedAt);
