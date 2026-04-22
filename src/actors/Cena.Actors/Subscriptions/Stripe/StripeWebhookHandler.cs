// =============================================================================
// Cena Platform — StripeWebhookHandler (EPIC-PRR-I PRR-301, ADR-0053)
//
// Receive Stripe webhook event, verify signature, map to SubscriptionCommands.
//
// Events handled:
//   checkout.session.completed  → SubscriptionActivated_V1
//   invoice.paid                → RenewalProcessed_V1 (subscription renewal)
//   invoice.payment_failed      → PaymentFailed_V1
//   customer.subscription.deleted → SubscriptionCancelled_V1
//   charge.refunded             → SubscriptionRefunded_V1
//
// Idempotency: Stripe event IDs are durable and unique. The handler tracks
// processed event ids via IProcessedWebhookLog so a replayed webhook is
// a no-op (logged + acked-OK). Webhook retries are safe.
//
// Signature verification is MANDATORY. Unsigned or mis-signed bodies throw
// before any command runs.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Stripe;
using Stripe.Checkout;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Tracks Stripe event ids we've already processed, so webhook retries are
/// a no-op. Keyed by Stripe's <c>evt_...</c> id.
/// </summary>
public interface IProcessedWebhookLog
{
    /// <summary>Returns true if this event id has not been seen before.</summary>
    Task<bool> TryRegisterAsync(string eventId, CancellationToken ct);
}

/// <summary>
/// Main entry point for processing Stripe webhook payloads. Callers
/// (the webhook endpoint) pass the raw body + the <c>Stripe-Signature</c>
/// header; this class verifies, parses, and dispatches.
/// </summary>
public sealed class StripeWebhookHandler
{
    private readonly StripeOptions _options;
    private readonly ISubscriptionAggregateStore _store;
    private readonly IProcessedWebhookLog _processedLog;
    private readonly TimeProvider _clock;

    public StripeWebhookHandler(
        StripeOptions options,
        ISubscriptionAggregateStore store,
        IProcessedWebhookLog processedLog,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _processedLog = processedLog ?? throw new ArgumentNullException(nameof(processedLog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Verify the Stripe signature, parse the body, and dispatch to the right
    /// event handler. Throws <see cref="StripeException"/> on signature
    /// failure (caller returns 400); returns <see cref="WebhookOutcome.Handled"/>
    /// on success, <see cref="WebhookOutcome.Duplicate"/> on replay.
    /// </summary>
    public async Task<WebhookOutcome> HandleAsync(
        string rawBody, string stripeSignatureHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            throw new ArgumentException("Webhook body required.", nameof(rawBody));
        }
        if (string.IsNullOrWhiteSpace(stripeSignatureHeader))
        {
            throw new ArgumentException("Stripe-Signature header required.", nameof(stripeSignatureHeader));
        }

        // Signature verification is Stripe's library — throws StripeException
        // on any mismatch. No silent-accept path.
        var stripeEvent = EventUtility.ConstructEvent(
            rawBody, stripeSignatureHeader, _options.WebhookSigningSecret);

        // Idempotency: skip already-processed events.
        var fresh = await _processedLog.TryRegisterAsync(stripeEvent.Id, ct);
        if (!fresh)
        {
            return WebhookOutcome.Duplicate;
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent, ct);
                break;
            case "invoice.paid":
                await HandleInvoicePaidAsync(stripeEvent, ct);
                break;
            case "invoice.payment_failed":
                await HandlePaymentFailedAsync(stripeEvent, ct);
                break;
            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent, ct);
                break;
            case "charge.refunded":
                await HandleChargeRefundedAsync(stripeEvent, ct);
                break;
            default:
                return WebhookOutcome.Ignored;
        }

        return WebhookOutcome.Handled;
    }

    // ----- event handlers -----

    private async Task HandleCheckoutCompletedAsync(Event ev, CancellationToken ct)
    {
        var session = (Session?)ev.Data.Object
            ?? throw new InvalidOperationException("checkout.session.completed missing session body.");

        var parentId = RequireMeta(session.Metadata, "cena_parent_id");
        var studentId = RequireMeta(session.Metadata, "cena_primary_student_id");
        var tier = ParseTier(RequireMeta(session.Metadata, "cena_tier"));
        var cycle = ParseCycle(RequireMeta(session.Metadata, "cena_cycle"));

        var aggregate = await _store.LoadAsync(parentId, ct);
        if (aggregate.State.Status != SubscriptionStatus.Unsubscribed)
        {
            return;   // Already activated — webhook retry after dedup passed; no-op
        }

        var evt = SubscriptionCommands.Activate(
            aggregate.State, parentId, studentId, tier, cycle,
            paymentTransactionIdEncrypted: session.Id, activatedAt: _clock.GetUtcNow());
        await _store.AppendAsync(parentId, evt, ct);
    }

    private async Task HandleInvoicePaidAsync(Event ev, CancellationToken ct)
    {
        var invoice = (Invoice?)ev.Data.Object
            ?? throw new InvalidOperationException("invoice.paid missing body.");
        var parentId = invoice.SubscriptionDetails?.Metadata is { Count: > 0 } meta
            ? RequireMeta(meta, "cena_parent_id")
            : invoice.Metadata is { Count: > 0 } m ? RequireMeta(m, "cena_parent_id") : null;
        if (string.IsNullOrWhiteSpace(parentId)) return;

        var aggregate = await _store.LoadAsync(parentId, ct);
        // Only emit renewal if already active; first-invoice payment is handled by checkout.session.completed.
        if (aggregate.State.Status != SubscriptionStatus.Active &&
            aggregate.State.Status != SubscriptionStatus.PastDue)
        {
            return;
        }
        var now = _clock.GetUtcNow();
        var nextRenewsAt = SubscriptionCommands.ComputeNextRenewal(now, aggregate.State.CurrentCycle);
        var evt = new RenewalProcessed_V1(
            ParentSubjectIdEncrypted: parentId,
            PaymentTransactionIdEncrypted: invoice.Id ?? string.Empty,
            GrossAmountAgorot: invoice.AmountPaid,
            RenewedAt: now,
            NextRenewsAt: nextRenewsAt);
        await _store.AppendAsync(parentId, evt, ct);
    }

    private async Task HandlePaymentFailedAsync(Event ev, CancellationToken ct)
    {
        var invoice = (Invoice?)ev.Data.Object
            ?? throw new InvalidOperationException("invoice.payment_failed missing body.");
        var parentId = invoice.SubscriptionDetails?.Metadata is { Count: > 0 } meta
            ? RequireMeta(meta, "cena_parent_id")
            : invoice.Metadata is { Count: > 0 } m ? RequireMeta(m, "cena_parent_id") : null;
        if (string.IsNullOrWhiteSpace(parentId)) return;

        var aggregate = await _store.LoadAsync(parentId, ct);
        if (aggregate.State.Status != SubscriptionStatus.Active &&
            aggregate.State.Status != SubscriptionStatus.PastDue)
        {
            return;
        }
        var evt = new PaymentFailed_V1(
            ParentSubjectIdEncrypted: parentId,
            Reason: invoice.LastFinalizationError?.Message ?? "stripe:payment_failed",
            AttemptNumber: (int)(invoice.AttemptCount + 1),
            FailedAt: _clock.GetUtcNow());
        await _store.AppendAsync(parentId, evt, ct);
    }

    private async Task HandleSubscriptionDeletedAsync(Event ev, CancellationToken ct)
    {
        var sub = (global::Stripe.Subscription?)ev.Data.Object
            ?? throw new InvalidOperationException("customer.subscription.deleted missing body.");
        if (sub.Metadata is not { Count: > 0 } meta) return;
        var parentId = RequireMeta(meta, "cena_parent_id");

        var aggregate = await _store.LoadAsync(parentId, ct);
        if (aggregate.State.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Refunded)
        {
            return;
        }
        var evt = SubscriptionCommands.Cancel(aggregate.State,
            reason: "stripe:subscription_deleted", initiator: "gateway", now: _clock.GetUtcNow());
        await _store.AppendAsync(parentId, evt, ct);
    }

    private async Task HandleChargeRefundedAsync(Event ev, CancellationToken ct)
    {
        var charge = (Charge?)ev.Data.Object
            ?? throw new InvalidOperationException("charge.refunded missing body.");
        if (charge.Metadata is not { Count: > 0 } meta) return;
        if (!meta.TryGetValue("cena_parent_id", out var parentId) || string.IsNullOrWhiteSpace(parentId))
        {
            return;
        }

        var aggregate = await _store.LoadAsync(parentId, ct);
        if (aggregate.State.ActivatedAt is null) return;
        // Refund window is enforced by the command; if Stripe refund lands
        // outside our window (e.g., merchant-initiated post-30d) we still
        // reflect the accounting but the command may refuse — in that case
        // surface a Cancel instead so state is not left stale.
        try
        {
            var evt = SubscriptionCommands.Refund(
                aggregate.State, charge.AmountRefunded, "stripe:charge_refunded", _clock.GetUtcNow());
            await _store.AppendAsync(parentId, evt, ct);
        }
        catch (SubscriptionCommandException)
        {
            var cancelEvt = SubscriptionCommands.Cancel(
                aggregate.State, "stripe:late_refund", "gateway", _clock.GetUtcNow());
            await _store.AppendAsync(parentId, cancelEvt, ct);
        }
    }

    private static string RequireMeta(IDictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Stripe webhook metadata missing '{key}'.");

    private static SubscriptionTier ParseTier(string v) =>
        Enum.TryParse<SubscriptionTier>(v, ignoreCase: true, out var t)
            ? t
            : throw new InvalidOperationException($"Unknown tier '{v}' in Stripe metadata.");

    private static BillingCycle ParseCycle(string v) =>
        Enum.TryParse<BillingCycle>(v, ignoreCase: true, out var c) && c != BillingCycle.None
            ? c
            : throw new InvalidOperationException($"Unknown cycle '{v}' in Stripe metadata.");
}

/// <summary>Result of processing a webhook event.</summary>
public enum WebhookOutcome
{
    /// <summary>Event was new and dispatched to a handler.</summary>
    Handled,
    /// <summary>Event was a replay; processed log dedup'd it.</summary>
    Duplicate,
    /// <summary>Event type is not in our interest list; acked but not acted.</summary>
    Ignored,
}
