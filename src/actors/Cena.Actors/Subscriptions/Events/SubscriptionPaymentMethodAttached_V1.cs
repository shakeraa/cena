// =============================================================================
// Cena Platform — SubscriptionPaymentMethodAttached_V1 (Phase 1D-fix item 2)
//
// Emitted on the parent's subscription stream after a SetupIntent verifies
// successfully and a card-on-file is captured. Pinning this event onto the
// stream lets the conversion-to-paid flow reuse the same payment method
// without re-prompting the user — the entire point of using SetupIntent
// over Checkout for trial-card-collection.
//
// Why a distinct event instead of overloading TrialStarted_V1:
//   * Payment method may attach BEFORE TrialStarted (start-trial flow) but
//     also AFTER conversion (parent-managed payment-method update).
//   * The event is also meaningful for non-trialling parents who attach a
//     method via the Account → Billing UI (Phase 3 scope).
//   * Crypto-shred symmetry: shred targets PaymentMethodIdEncrypted.
//
// Encryption convention (ADR-0038):
//   * PaymentMethodIdEncrypted is the wire-encrypted Stripe pm_… id.
//   * FingerprintHash is the SHA-256 of Stripe's card.fingerprint — already
//     one-way and not PII; persisted bare for ledger join correctness.
//
// Idempotency: callers append at most once per (parentSubjectId, fingerprint)
// pair; the start-trial handler de-duplicates by checking
// SubscriptionState.LastAttachedPaymentMethodFingerprintHash before append.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Provenance of a payment-method attach event. Drives analytics and surfaces
/// to ops "where did this card come from?" without leaking PII.
/// </summary>
public enum PaymentMethodAttachSource
{
    /// <summary>SetupIntent re-read on the trial-start path (Phase 1D).</summary>
    TrialStartSetupIntent = 1,

    /// <summary>SetupIntent re-read from the Account → Billing UI (Phase 3 scope).</summary>
    AccountBillingSetupIntent = 2,

    /// <summary>Captured during a Stripe Checkout completion (Phase 3 scope).</summary>
    CheckoutCompletion = 3,
}

/// <summary>
/// Payment method attached to a parent's subscription. Emitted on
/// <c>subscription-{parentSubjectIdEncrypted}</c> after a successful
/// <c>SetupIntent.Verify</c> when the card has not been previously attached
/// to this stream.
/// </summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent subject id (ADR-0038).</param>
/// <param name="PaymentMethodIdEncrypted">
/// Wire-encrypted Stripe payment method id (<c>pm_…</c>). Conversion flow
/// passes this to <c>Subscriptions.Create</c> so the user is not re-prompted.
/// </param>
/// <param name="FingerprintHash">
/// SHA-256 of Stripe <c>card.fingerprint</c>. Same value persisted on
/// <see cref="TrialStarted_V1.FingerprintHash"/> when the attach happened on
/// the trial-start path. Used as the idempotency key — re-attaching the same
/// card emits no new event.
/// </param>
/// <param name="AttachedAt">Wall-clock attach instant (UTC).</param>
/// <param name="Source">Provenance of the attach (see <see cref="PaymentMethodAttachSource"/>).</param>
public sealed record SubscriptionPaymentMethodAttached_V1(
    string ParentSubjectIdEncrypted,
    string PaymentMethodIdEncrypted,
    string FingerprintHash,
    DateTimeOffset AttachedAt,
    PaymentMethodAttachSource Source);
