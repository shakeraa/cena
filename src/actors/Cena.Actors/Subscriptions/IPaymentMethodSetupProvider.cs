// =============================================================================
// Cena Platform — IPaymentMethodSetupProvider (Phase 1C, trial-then-paywall §4.0)
//
// Port for "tokenize a payment method without charging" — the seam used by the
// trial-start flow to collect a card-on-file via Stripe SetupIntent and surface
// a stable card.fingerprint to the trial-fingerprint ledger (§5.7) without
// ever capturing card-bearer data on Cena's side.
//
// Two operations:
//   1. CreateSetupIntentAsync   — server starts a SetupIntent; SPA's Stripe
//                                 Elements integration consumes the
//                                 client_secret to confirm the card.
//   2. VerifyAndExtractFingerprintAsync
//                               — server-side re-read (single source of truth
//                                 per §5.14). The client's "I confirmed" claim
//                                 is informational only; this method performs
//                                 the authoritative GET and returns the
//                                 status + fingerprint from Stripe.
//
// Why distinct from ICheckoutSessionProvider:
//   - Checkout creates a Stripe Subscription synchronously at end-of-flow
//     (state-bearing for activation; `checkout.session.completed` IS the
//     activation source-of-truth).
//   - SetupIntent creates *no* subscription; it tokenises a payment method
//     and the trial timer is owned by Cena. The webhook for SetupIntent is
//     informational only — `start-trial` is the only path that can append
//     `TrialStarted_V1` (§5.14).
//
// Five failure modes (§4.0.1) — each must be a distinct enum status with a
// caller-decidable UX:
//
//   Stripe status                       → SetupIntentStatus  → UX hint
//   ────────────────────────────────────────────────────────────────────────
//   succeeded                           → Succeeded            proceed; fingerprint extracted
//   requires_action                     → RequiresAction       SPA completes 3DS
//   requires_payment_method             → RequiresPaymentMethod card declined; surface error
//   processing                          → Pending              poll again ~2s
//   anything else (canceled, etc)       → Failed               hard error
//
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Coarse status of a SetupIntent verification — maps Stripe's wire-level
/// statuses to a small caller-decidable enum so the trial-start endpoint
/// can return a clean response shape per §4.0.1 / §5.14.
/// </summary>
public enum SetupIntentStatus
{
    /// <summary>
    /// SetupIntent confirmed; <see cref="SetupIntentVerifyResult.CardFingerprint"/>
    /// and <see cref="SetupIntentVerifyResult.PaymentMethodId"/> are populated.
    /// Caller proceeds to ledger check + trial event append.
    /// </summary>
    Succeeded,

    /// <summary>
    /// 3DS challenge still pending (Stripe <c>requires_action</c>).
    /// Caller returns 202 with <c>retry_after_ms</c>; SPA polls.
    /// </summary>
    RequiresAction,

    /// <summary>
    /// Card declined for setup (Stripe <c>requires_payment_method</c>) — SPA
    /// surfaces a "card was declined" error; user re-enters or picks another
    /// card. No state change in Cena.
    /// </summary>
    RequiresPaymentMethod,

    /// <summary>
    /// Stripe is still processing the confirm (Stripe <c>processing</c>) —
    /// transient, usually clears in &lt;2s. Caller polls again.
    /// </summary>
    Pending,

    /// <summary>
    /// Terminal failure (Stripe <c>canceled</c>, unknown status, or
    /// authentication failure after retries). Hard error; caller surfaces
    /// a generic failure UX and offers another card.
    /// </summary>
    Failed,
}

/// <summary>
/// Input to <see cref="IPaymentMethodSetupProvider.CreateSetupIntentAsync"/>.
/// </summary>
/// <param name="ParentSubjectIdEncrypted">
/// Encrypted parent identifier — embedded in SetupIntent metadata so the
/// informational webhook handler can correlate without a secondary lookup.
/// </param>
/// <param name="IdempotencyKey">
/// Stripe idempotency key — a replay with the same key returns the same
/// SetupIntent rather than creating a duplicate. SPA generates this once
/// per trial-start attempt and reuses across retries.
/// </param>
public sealed record SetupIntentInitRequest(
    string ParentSubjectIdEncrypted,
    string IdempotencyKey);

/// <summary>
/// Output of <see cref="IPaymentMethodSetupProvider.CreateSetupIntentAsync"/>.
/// </summary>
/// <param name="SetupIntentId">Stripe id (<c>seti_…</c>) of the created SetupIntent.</param>
/// <param name="ClientSecret">
/// Client secret — surfaced to the SPA's Stripe Elements integration so the
/// browser can confirm the card. Format is <c>seti_…_secret_…</c> per Stripe
/// spec; never null/empty on a successful create.
/// </param>
/// <param name="Status">
/// SetupIntent status at create-time — typically
/// <see cref="SetupIntentStatus.RequiresPaymentMethod"/> (waiting for the
/// SPA to attach a card) or <see cref="SetupIntentStatus.RequiresAction"/>
/// for confirmation-token-driven flows.
/// </param>
public sealed record SetupIntentInitResult(
    string SetupIntentId,
    string ClientSecret,
    SetupIntentStatus Status);

/// <summary>
/// Output of <see cref="IPaymentMethodSetupProvider.VerifyAndExtractFingerprintAsync"/>.
/// </summary>
/// <param name="Status">Coarse status — see <see cref="SetupIntentStatus"/>.</param>
/// <param name="CardFingerprint">
/// Stripe <c>payment_method.card.fingerprint</c> — stable per-card across
/// emails (Stripe spec). Populated only when <see cref="Status"/> is
/// <see cref="SetupIntentStatus.Succeeded"/>; null otherwise.
/// </param>
/// <param name="PaymentMethodId">
/// Stripe <c>pm_…</c> id — populated only when <see cref="Status"/> is
/// <see cref="SetupIntentStatus.Succeeded"/>; the trial-start handler stores
/// it (encrypted) so conversion can reuse the saved card without re-collecting.
/// </param>
/// <param name="DeclineCode">
/// Stripe <c>last_setup_error.decline_code</c> when present (e.g.,
/// <c>insufficient_funds</c>, <c>card_velocity_exceeded</c>) — surfaced as
/// a hint for product analytics / dashboard tile 6 (§5.22). Null when
/// <see cref="Status"/> is <see cref="SetupIntentStatus.Succeeded"/> or
/// when Stripe did not return a decline code.
/// </param>
public sealed record SetupIntentVerifyResult(
    SetupIntentStatus Status,
    string? CardFingerprint,
    string? PaymentMethodId,
    string? DeclineCode);

/// <summary>
/// Port for tokenising a card-on-file via Stripe SetupIntent without charging.
/// Two implementations:
///   * <c>StripeSetupIntentProvider</c>  — production adapter (Stripe.Net SDK).
///   * <c>InMemorySetupIntentProvider</c> — test/dev fake; deterministic
///     fingerprint via SHA256(<c>"test-card-" + last4</c>) per
///     <c>trial-then-paywall §5.25</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per <c>trial-then-paywall §5.14</c> (server-side re-read rule):
/// <see cref="VerifyAndExtractFingerprintAsync"/> MUST issue a Stripe
/// <c>SetupIntents.Get</c> call — never trust client-supplied state. The SPA
/// confirms via Stripe Elements which only sets browser-side state; the
/// trial-start handler treats the verify result as the authoritative source.
/// </para>
/// <para>
/// Idempotency contract:
///   Calling <see cref="CreateSetupIntentAsync"/> with a previously-seen
///   <see cref="SetupIntentInitRequest.IdempotencyKey"/> returns the same
///   SetupIntent (Stripe-side idempotency on the create endpoint). Duplicate
///   <see cref="VerifyAndExtractFingerprintAsync"/> calls are naturally
///   idempotent (server-side GET).
/// </para>
/// </remarks>
public interface IPaymentMethodSetupProvider
{
    /// <summary>Adapter name for metrics/audit (<c>"stripe"</c> | <c>"in-memory"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Create a Stripe SetupIntent with <c>usage = "off_session"</c> and
    /// <c>payment_method_types = ["card"]</c>. Returns the
    /// <see cref="SetupIntentInitResult.ClientSecret"/> for the SPA's Stripe
    /// Elements integration to confirm against.
    /// </summary>
    /// <param name="request">Input — see <see cref="SetupIntentInitRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SetupIntentInitResult> CreateSetupIntentAsync(
        SetupIntentInitRequest request, CancellationToken ct);

    /// <summary>
    /// Server-side re-read of the SetupIntent — the single source of truth
    /// per §5.14. Returns the coarse <see cref="SetupIntentStatus"/> and,
    /// for <see cref="SetupIntentStatus.Succeeded"/>, the stable
    /// <see cref="SetupIntentVerifyResult.CardFingerprint"/> +
    /// <see cref="SetupIntentVerifyResult.PaymentMethodId"/>.
    /// </summary>
    /// <param name="setupIntentId">Stripe id (<c>seti_…</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SetupIntentVerifyResult> VerifyAndExtractFingerprintAsync(
        string setupIntentId, CancellationToken ct);
}
