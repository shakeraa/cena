// =============================================================================
// Cena Platform — StripeWebhookHandler tests (EPIC-PRR-I PRR-301)
//
// Signature-verification is tested by relying on Stripe.net's real
// EventUtility.ConstructEvent. We build a properly-signed payload and
// verify the handler processes it; then we tamper and verify rejection.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Actors.Subscriptions.Stripe;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions.Stripe;

public class StripeWebhookHandlerTests
{
    private const string WebhookSecret = "whsec_test_secret_placeholder_for_unit_tests";
    private readonly StripeOptions _options = new()
    {
        SecretKey = "sk_test_xxx",
        WebhookSigningSecret = WebhookSecret,
        PriceIds = AllPrices(),
    };

    [Fact]
    public async Task Valid_checkout_completed_creates_subscription_activation_event()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var log = new InMemoryProcessedWebhookLog();
        var handler = new StripeWebhookHandler(_options, store, log,
            TimeProvider.System);

        var body = BuildCheckoutCompletedBody(
            eventId: "evt_test_01",
            sessionId: "cs_test_01",
            parentId: "enc::parent::1",
            studentId: "enc::student::1",
            tier: "Premium",
            cycle: "Monthly");
        var sig = BuildStripeSignature(body, WebhookSecret);

        var outcome = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Handled, outcome);

        var events = await store.ReadEventsAsync("enc::parent::1", CancellationToken.None);
        Assert.Single(events);
        Assert.IsType<SubscriptionActivated_V1>(events[0]);
    }

    [Fact]
    public async Task Replayed_event_with_same_id_is_duplicate()
    {
        var store = new InMemorySubscriptionAggregateStore();
        var log = new InMemoryProcessedWebhookLog();
        var handler = new StripeWebhookHandler(_options, store, log, TimeProvider.System);

        var body = BuildCheckoutCompletedBody(
            eventId: "evt_replay_01", sessionId: "cs_1",
            parentId: "enc::p::2", studentId: "enc::s::2",
            tier: "Premium", cycle: "Monthly");
        var sig = BuildStripeSignature(body, WebhookSecret);

        await handler.HandleAsync(body, sig, CancellationToken.None);
        var outcome2 = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Duplicate, outcome2);

        var events = await store.ReadEventsAsync("enc::p::2", CancellationToken.None);
        Assert.Single(events);   // no double-activation
    }

    [Fact]
    public async Task Invalid_signature_throws()
    {
        var handler = new StripeWebhookHandler(
            _options, new InMemorySubscriptionAggregateStore(),
            new InMemoryProcessedWebhookLog(), TimeProvider.System);

        var body = BuildCheckoutCompletedBody(
            "evt_bad_sig", "cs_x", "enc::p::x", "enc::s::x", "Basic", "Monthly");
        var badSig = "t=1,v1=deadbeef";

        await Assert.ThrowsAsync<global::Stripe.StripeException>(() =>
            handler.HandleAsync(body, badSig, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_body_throws()
    {
        var handler = new StripeWebhookHandler(
            _options, new InMemorySubscriptionAggregateStore(),
            new InMemoryProcessedWebhookLog(), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync("", "t=1,v1=x", CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_event_type_returns_ignored()
    {
        var handler = new StripeWebhookHandler(
            _options, new InMemorySubscriptionAggregateStore(),
            new InMemoryProcessedWebhookLog(), TimeProvider.System);

        var body = BuildUnknownEventBody("evt_unknown", "ping.pong");
        var sig = BuildStripeSignature(body, WebhookSecret);
        var outcome = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Ignored, outcome);
    }

    [Fact]
    public async Task Payment_failed_event_emits_PaymentFailed_and_drives_PastDue()
    {
        // Activate first so the failed-payment handler has a non-Unsubscribed
        // state to transition from. PaymentFailed_V1 is only emitted when the
        // subscription is already Active or already PastDue (retry attempts).
        var (handler, store, _) = await NewActivatedAsync("enc::p::pf", "enc::s::pf");

        var body = BuildInvoicePaymentFailedBody(
            eventId: "evt_pf_1",
            parentId: "enc::p::pf",
            attemptCount: 1,
            failureMessage: "card_declined");
        var sig = BuildStripeSignature(body, WebhookSecret);

        var outcome = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Handled, outcome);

        var events = await store.ReadEventsAsync("enc::p::pf", CancellationToken.None);
        // Activation event + the new PaymentFailed_V1 event.
        Assert.Equal(2, events.Count);
        var failed = Assert.IsType<PaymentFailed_V1>(events[1]);
        Assert.Equal("enc::p::pf", failed.ParentSubjectIdEncrypted);
        Assert.Equal("card_declined", failed.Reason);

        // State machine: Active + PaymentFailed_V1 → PastDue.
        var aggregate = await store.LoadAsync("enc::p::pf", CancellationToken.None);
        Assert.Equal(SubscriptionStatus.PastDue, aggregate.State.Status);
    }

    [Fact]
    public async Task Refund_event_emits_SubscriptionRefunded()
    {
        var (handler, store, _) = await NewActivatedAsync("enc::p::rf", "enc::s::rf");

        var body = BuildChargeRefundedBody(
            eventId: "evt_rf_1",
            parentId: "enc::p::rf",
            amountRefundedAgorot: 2999L);
        var sig = BuildStripeSignature(body, WebhookSecret);

        var outcome = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Handled, outcome);

        var events = await store.ReadEventsAsync("enc::p::rf", CancellationToken.None);
        Assert.Equal(2, events.Count);
        var refunded = Assert.IsType<SubscriptionRefunded_V1>(events[1]);
        Assert.Equal("enc::p::rf", refunded.ParentSubjectIdEncrypted);
        Assert.Equal(2999L, refunded.RefundedAmountAgorot);

        var aggregate = await store.LoadAsync("enc::p::rf", CancellationToken.None);
        Assert.Equal(SubscriptionStatus.Refunded, aggregate.State.Status);
    }

    [Fact]
    public async Task Subscription_deleted_event_emits_Cancelled()
    {
        var (handler, store, _) = await NewActivatedAsync("enc::p::ds", "enc::s::ds");

        var body = BuildSubscriptionDeletedBody(
            eventId: "evt_ds_1",
            parentId: "enc::p::ds",
            subscriptionId: "sub_test_ds");
        var sig = BuildStripeSignature(body, WebhookSecret);

        var outcome = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Handled, outcome);

        var events = await store.ReadEventsAsync("enc::p::ds", CancellationToken.None);
        Assert.Equal(2, events.Count);
        var cancelled = Assert.IsType<SubscriptionCancelled_V1>(events[1]);
        Assert.Equal("enc::p::ds", cancelled.ParentSubjectIdEncrypted);

        var aggregate = await store.LoadAsync("enc::p::ds", CancellationToken.None);
        Assert.Equal(SubscriptionStatus.Cancelled, aggregate.State.Status);
    }

    // ----- helpers -----

    private async Task<(StripeWebhookHandler Handler, InMemorySubscriptionAggregateStore Store, InMemoryProcessedWebhookLog Log)>
        NewActivatedAsync(string parentId, string studentId)
    {
        var store = new InMemorySubscriptionAggregateStore();
        var log = new InMemoryProcessedWebhookLog();
        var handler = new StripeWebhookHandler(_options, store, log, TimeProvider.System);
        var body = BuildCheckoutCompletedBody(
            eventId: "evt_activate_" + parentId,
            sessionId: "cs_activate_" + parentId,
            parentId: parentId,
            studentId: studentId,
            tier: "Premium",
            cycle: "Monthly");
        var sig = BuildStripeSignature(body, WebhookSecret);
        var outcome = await handler.HandleAsync(body, sig, CancellationToken.None);
        Assert.Equal(WebhookOutcome.Handled, outcome);
        return (handler, store, log);
    }

    private static StripePriceIdMap AllPrices() => new()
    {
        BasicMonthly = "p_bm", BasicAnnual = "p_ba",
        PlusMonthly = "p_pm", PlusAnnual = "p_pa",
        PremiumMonthly = "p_prm", PremiumAnnual = "p_pra",
    };

    private static string BuildCheckoutCompletedBody(
        string eventId, string sessionId,
        string parentId, string studentId,
        string tier, string cycle)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2025-02-24.acacia",
          "created": {{created}},
          "livemode": false,
          "pending_webhooks": 0,
          "request": { "id": null, "idempotency_key": null },
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "client_reference_id": "{{parentId}}",
              "metadata": {
                "cena_parent_id": "{{parentId}}",
                "cena_primary_student_id": "{{studentId}}",
                "cena_tier": "{{tier}}",
                "cena_cycle": "{{cycle}}"
              }
            }
          }
        }
        """;
    }

    private static string BuildInvoicePaymentFailedBody(
        string eventId, string parentId, int attemptCount, string failureMessage)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // `subscription_details.metadata.cena_parent_id` is the canonical
        // location the handler reads; fall-back to `metadata.cena_parent_id`
        // exists in code but we exercise the primary path.
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2025-02-24.acacia",
          "created": {{created}},
          "livemode": false,
          "pending_webhooks": 0,
          "request": { "id": null, "idempotency_key": null },
          "type": "invoice.payment_failed",
          "data": {
            "object": {
              "id": "in_test_pf",
              "object": "invoice",
              "attempt_count": {{attemptCount}},
              "amount_paid": 0,
              "subscription_details": {
                "metadata": { "cena_parent_id": "{{parentId}}" }
              },
              "last_finalization_error": { "message": "{{failureMessage}}" }
            }
          }
        }
        """;
    }

    private static string BuildChargeRefundedBody(
        string eventId, string parentId, long amountRefundedAgorot)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2025-02-24.acacia",
          "created": {{created}},
          "livemode": false,
          "pending_webhooks": 0,
          "request": { "id": null, "idempotency_key": null },
          "type": "charge.refunded",
          "data": {
            "object": {
              "id": "ch_test_rf",
              "object": "charge",
              "amount_refunded": {{amountRefundedAgorot}},
              "metadata": { "cena_parent_id": "{{parentId}}" }
            }
          }
        }
        """;
    }

    private static string BuildSubscriptionDeletedBody(
        string eventId, string parentId, string subscriptionId)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2025-02-24.acacia",
          "created": {{created}},
          "livemode": false,
          "pending_webhooks": 0,
          "request": { "id": null, "idempotency_key": null },
          "type": "customer.subscription.deleted",
          "data": {
            "object": {
              "id": "{{subscriptionId}}",
              "object": "subscription",
              "metadata": { "cena_parent_id": "{{parentId}}" }
            }
          }
        }
        """;
    }

    private static string BuildUnknownEventBody(string eventId, string type)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2025-02-24.acacia",
          "created": {{created}},
          "livemode": false,
          "pending_webhooks": 0,
          "request": { "id": null, "idempotency_key": null },
          "type": "{{type}}",
          "data": { "object": {} }
        }
        """;
    }

    /// <summary>
    /// Build a valid Stripe-Signature header value for the given body. Mirrors
    /// Stripe's signing protocol: <c>t=&lt;unix&gt;,v1=&lt;hmac-sha256 of "t.body"&gt;</c>.
    /// </summary>
    private static string BuildStripeSignature(string body, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"t={timestamp},v1={hex}";
    }
}
