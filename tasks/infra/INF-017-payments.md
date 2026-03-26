# INF-017: Payment & Subscription System (Stripe + PayPlus)

**Priority:** P0 — BLOCKER (no payments = no revenue)
**Blocked by:** SEC-009 (Firebase Auth — user identity for billing)
**Blocks:** Premium tier features, school B2B billing, go-to-market
**Estimated effort:** 7 days
**Contract:** `contracts/backend/actor-contracts.cs` (tier: free/premium), `contracts/frontend/signalr-messages.ts` (DeviceContext, tier)

---

## Context
Cena operates a freemium model in Israel: free tier (limited daily LLM budget, 25K output tokens/day) and premium tier (expanded budget, priority LLM routing, teacher dashboards). Israeli market requires dual payment gateway: Stripe (international cards, Apple Pay, Google Pay) and PayPlus (Israeli Shva network, local credit cards, bit payments). Israeli tax law requires `heshbonit mas` (tax invoice) with specific fields. Subscription lifecycle (trial, active, past_due, canceled) drives feature gating via Firebase custom claims (`tier: "free" | "premium"`).

## Subtasks

### INF-017.1: Stripe + PayPlus Integration
**Files:**
- `src/Cena.Payments/Providers/StripePaymentProvider.cs` — Stripe integration
- `src/Cena.Payments/Providers/PayPlusPaymentProvider.cs` — PayPlus integration
- `src/Cena.Payments/Providers/IPaymentProvider.cs` — provider interface
- `src/Cena.Payments/PaymentRouter.cs` — routes to correct provider based on payment method
- `src/Cena.Payments/Models/PaymentModels.cs` — shared models

**Acceptance:**
- [ ] `IPaymentProvider` interface: `CreateCheckoutSession`, `CancelSubscription`, `RefundPayment`, `GetPaymentStatus`
- [ ] Stripe: supports `card`, `apple_pay`, `google_pay` payment methods
- [ ] PayPlus: supports Israeli credit cards (Visa/Mastercard/AmEx via Shva), `bit` payments
- [ ] `PaymentRouter` selects provider: Israeli phone number + Israeli card → PayPlus; everything else → Stripe
- [ ] Checkout session creates Stripe/PayPlus session URL, client redirects
- [ ] All amounts in ILS (Israeli Shekel), converted to USD for Stripe international
- [ ] Prices: Premium monthly ₪49.90/month, Premium annual ₪399/year (33% discount)
- [ ] School B2B: custom pricing per school, minimum 50 seats, invoiced monthly
- [ ] Idempotency: `CreateCheckoutSession` with same `idempotencyKey` returns same session
- [ ] No payment credentials in source code — Stripe key and PayPlus API key from environment/secrets

**Test:**
```csharp
[Fact]
public async Task Stripe_CreateCheckoutSession_ReturnsUrl()
{
    var provider = new StripePaymentProvider(_stripeTestKey);
    var session = await provider.CreateCheckoutSessionAsync(new CheckoutRequest
    {
        StudentId = "student-pay-1",
        PlanId = "premium-monthly",
        Currency = "ILS",
        AmountInCents = 4990, // ₪49.90
        SuccessUrl = "https://app.cena.co.il/payment/success",
        CancelUrl = "https://app.cena.co.il/payment/cancel",
        IdempotencyKey = "idem-001"
    });

    Assert.NotNull(session.CheckoutUrl);
    Assert.StartsWith("https://checkout.stripe.com/", session.CheckoutUrl);
    Assert.NotNull(session.SessionId);
}

[Fact]
public async Task Stripe_IdempotentCheckout_ReturnsSameSession()
{
    var provider = new StripePaymentProvider(_stripeTestKey);
    var request = new CheckoutRequest
    {
        StudentId = "student-pay-2",
        PlanId = "premium-annual",
        Currency = "ILS",
        AmountInCents = 39900,
        IdempotencyKey = "idem-002"
    };

    var session1 = await provider.CreateCheckoutSessionAsync(request);
    var session2 = await provider.CreateCheckoutSessionAsync(request);
    Assert.Equal(session1.SessionId, session2.SessionId);
}

[Fact]
public async Task PaymentRouter_SelectsPayPlusForIsraeliCard()
{
    var router = new PaymentRouter(_stripeProvider, _payPlusProvider);
    var provider = router.SelectProvider(new PaymentContext
    {
        PhoneCountryCode = "+972",
        CardBin = "458900", // Israeli Visa
        PreferredMethod = "card"
    });

    Assert.IsType<PayPlusPaymentProvider>(provider);
}

[Fact]
public async Task PaymentRouter_SelectsStripeForInternationalCard()
{
    var router = new PaymentRouter(_stripeProvider, _payPlusProvider);
    var provider = router.SelectProvider(new PaymentContext
    {
        PhoneCountryCode = "+1",
        CardBin = "424242", // US Visa test
        PreferredMethod = "apple_pay"
    });

    Assert.IsType<StripePaymentProvider>(provider);
}

[Fact]
public async Task SuccessfulPayment_ReturnsConfirmation()
{
    var provider = new StripePaymentProvider(_stripeTestKey);
    // Simulate successful payment via Stripe test card
    var status = await provider.GetPaymentStatusAsync("cs_test_completed");
    Assert.Equal(PaymentStatus.Succeeded, status.Status);
    Assert.Equal(4990, status.AmountInCents);
}

[Fact]
public async Task FailedPayment_ReturnsDeclineReason()
{
    var provider = new StripePaymentProvider(_stripeTestKey);
    var status = await provider.GetPaymentStatusAsync("cs_test_declined");
    Assert.Equal(PaymentStatus.Failed, status.Status);
    Assert.NotNull(status.DeclineReason);
}
```

---

### INF-017.2: Webhook Handlers (Stripe + PayPlus)
**Files:**
- `src/Cena.Api/Webhooks/StripeWebhookController.cs` — Stripe webhook endpoint
- `src/Cena.Api/Webhooks/PayPlusWebhookController.cs` — PayPlus webhook endpoint
- `src/Cena.Payments/WebhookProcessor.cs` — shared webhook processing logic
- `src/Cena.Payments/Events/PaymentEvents.cs` — payment domain events

**Acceptance:**
- [ ] Stripe webhook endpoint: `POST /webhooks/stripe` with signature verification (`Stripe-Signature` header)
- [ ] PayPlus webhook endpoint: `POST /webhooks/payplus` with HMAC verification
- [ ] Webhook events handled: `checkout.session.completed`, `invoice.paid`, `invoice.payment_failed`, `customer.subscription.updated`, `customer.subscription.deleted`, `charge.refunded`
- [ ] Idempotent processing: webhook event ID stored in Redis with 24h TTL, duplicate events skipped
- [ ] On `checkout.session.completed` → update Firebase custom claims: `tier: "premium"`
- [ ] On `invoice.payment_failed` → set `tier: "free"` after 3 failed retries (dunning)
- [ ] On `customer.subscription.deleted` → set `tier: "free"`, emit `cena.school.events.SubscriptionCanceled`
- [ ] On `charge.refunded` → set `tier: "free"`, emit refund event
- [ ] Webhook processing logged with correlation ID for audit trail
- [ ] Failed webhook processing → retry queue (3 attempts, exponential backoff)

**Test:**
```csharp
[Fact]
public async Task StripeWebhook_CheckoutCompleted_UpgradesTier()
{
    var payload = CreateStripeEvent("checkout.session.completed", new
    {
        client_reference_id = "student-pay-3",
        subscription = "sub_test_123",
        amount_total = 4990
    });

    var response = await _client.PostAsync("/webhooks/stripe",
        new StringContent(payload, Encoding.UTF8, "application/json"));
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // Verify Firebase claims updated
    var claims = await _firebaseAdmin.GetUserAsync("student-pay-3");
    Assert.Equal("premium", claims.CustomClaims["tier"]);
}

[Fact]
public async Task StripeWebhook_PaymentFailed_DunningFlow()
{
    // Simulate 3 consecutive payment failures
    for (int i = 0; i < 3; i++)
    {
        var payload = CreateStripeEvent("invoice.payment_failed", new
        {
            customer = "cus_test_456",
            subscription = "sub_test_456",
            attempt_count = i + 1
        });
        await _client.PostAsync("/webhooks/stripe",
            new StringContent(payload, Encoding.UTF8, "application/json"));
    }

    // After 3 failures, tier downgraded
    var claims = await _firebaseAdmin.GetUserAsync("student-pay-4");
    Assert.Equal("free", claims.CustomClaims["tier"]);
}

[Fact]
public async Task StripeWebhook_DuplicateEvent_Skipped()
{
    var payload = CreateStripeEvent("checkout.session.completed", new
    {
        client_reference_id = "student-pay-5"
    }, eventId: "evt_duplicate_001");

    var r1 = await _client.PostAsync("/webhooks/stripe", new StringContent(payload));
    var r2 = await _client.PostAsync("/webhooks/stripe", new StringContent(payload));

    Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
    Assert.Equal(HttpStatusCode.OK, r2.StatusCode); // Accepted but not reprocessed

    // Firebase claims set only once (idempotent)
    var updateCount = _firebaseUpdateTracker.GetCount("student-pay-5");
    Assert.Equal(1, updateCount);
}

[Fact]
public async Task StripeWebhook_InvalidSignature_Returns401()
{
    var response = await _client.PostAsync("/webhooks/stripe",
        new StringContent("{\"type\":\"fake\"}", Encoding.UTF8, "application/json"));
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task StripeWebhook_Refund_DowngradesTier()
{
    var payload = CreateStripeEvent("charge.refunded", new
    {
        customer = "cus_test_789",
        amount = 4990
    });

    await _client.PostAsync("/webhooks/stripe", new StringContent(payload));

    var claims = await _firebaseAdmin.GetUserAsync("student-pay-6");
    Assert.Equal("free", claims.CustomClaims["tier"]);
}
```

---

### INF-017.3: Subscription Lifecycle Management
**Files:**
- `src/Cena.Payments/Subscriptions/SubscriptionManager.cs` — subscription state machine
- `src/Cena.Payments/Subscriptions/TrialManager.cs` — trial period management
- `src/Cena.Payments/Subscriptions/SubscriptionState.cs` — state enum + transitions
- `src/Cena.Api/Controllers/SubscriptionController.cs` — REST API for subscription management

**Acceptance:**
- [ ] Subscription states: `trialing` → `active` → `past_due` → `canceled` → `expired`
- [ ] Trial: 14-day free trial, no credit card required, full premium features
- [ ] Trial expiry: `tier` set to `free`, push notification "Your trial ended" via NATS `cena.outreach.commands.SendReminder`
- [ ] Grace period: 7 days after payment failure before downgrade (dunning emails at day 1, 3, 7)
- [ ] Upgrade: instant activation, prorated billing for remaining period
- [ ] Downgrade: effective at end of billing period, features retained until then
- [ ] Cancellation: immediate or end-of-period (student choice), data retained 90 days
- [ ] REST endpoints: `GET /api/subscription`, `POST /api/subscription/upgrade`, `POST /api/subscription/cancel`
- [ ] All state transitions emit NATS events on `cena.school.events.>` for school admin visibility

**Test:**
```csharp
[Fact]
public async Task Trial_StartsAutomatically_ExpiresAfter14Days()
{
    var manager = new SubscriptionManager(_store, _firebaseAdmin);
    var sub = await manager.StartTrialAsync("student-trial-1");

    Assert.Equal(SubscriptionState.Trialing, sub.State);
    Assert.Equal(DateTimeOffset.UtcNow.AddDays(14).Date, sub.TrialEndsAt.Value.Date);

    // Fast-forward past trial
    _clock.Advance(TimeSpan.FromDays(15));
    await manager.CheckExpiredTrialsAsync();

    var updated = await manager.GetSubscriptionAsync("student-trial-1");
    Assert.Equal(SubscriptionState.Expired, updated.State);

    // Firebase tier set to free
    var claims = await _firebaseAdmin.GetUserAsync("student-trial-1");
    Assert.Equal("free", claims.CustomClaims["tier"]);
}

[Fact]
public async Task Trial_UpgradeBeforeExpiry_BecomeActive()
{
    var manager = new SubscriptionManager(_store, _firebaseAdmin);
    await manager.StartTrialAsync("student-trial-2");

    _clock.Advance(TimeSpan.FromDays(7)); // Mid-trial

    await manager.UpgradeAsync("student-trial-2", "premium-monthly", "sub_stripe_001");
    var sub = await manager.GetSubscriptionAsync("student-trial-2");

    Assert.Equal(SubscriptionState.Active, sub.State);
    Assert.Null(sub.TrialEndsAt); // Trial consumed
}

[Fact]
public async Task PastDue_DowngradesAfterGracePeriod()
{
    var manager = new SubscriptionManager(_store, _firebaseAdmin);
    await manager.ActivateAsync("student-pd-1", "sub_001");

    // Payment fails
    await manager.HandlePaymentFailureAsync("student-pd-1");
    var sub1 = await manager.GetSubscriptionAsync("student-pd-1");
    Assert.Equal(SubscriptionState.PastDue, sub1.State);
    Assert.Equal("premium", (await _firebaseAdmin.GetUserAsync("student-pd-1")).CustomClaims["tier"]);

    // Grace period ends (7 days)
    _clock.Advance(TimeSpan.FromDays(8));
    await manager.CheckExpiredGracePeriodsAsync();

    var sub2 = await manager.GetSubscriptionAsync("student-pd-1");
    Assert.Equal(SubscriptionState.Canceled, sub2.State);
    Assert.Equal("free", (await _firebaseAdmin.GetUserAsync("student-pd-1")).CustomClaims["tier"]);
}

[Fact]
public async Task Cancel_EndOfPeriod_RetainsFeaturesUntilExpiry()
{
    var manager = new SubscriptionManager(_store, _firebaseAdmin);
    await manager.ActivateAsync("student-cancel-1", "sub_002");

    await manager.CancelAsync("student-cancel-1", CancelMode.EndOfPeriod);
    var sub = await manager.GetSubscriptionAsync("student-cancel-1");

    Assert.Equal(SubscriptionState.Active, sub.State); // Still active
    Assert.NotNull(sub.CancelsAt); // But scheduled to cancel
    Assert.Equal("premium", (await _firebaseAdmin.GetUserAsync("student-cancel-1")).CustomClaims["tier"]);
}
```

---

### INF-017.4: Israeli Tax Invoice (`Heshbonit Mas`)
**Files:**
- `src/Cena.Payments/Tax/IsraeliTaxInvoice.cs` — tax invoice model
- `src/Cena.Payments/Tax/InvoiceGenerator.cs` — PDF generation
- `src/Cena.Payments/Tax/IsraeliTaxRules.cs` — VAT calculation
- `src/Cena.Api/Controllers/InvoiceController.cs` — invoice download endpoint

**Acceptance:**
- [ ] Tax invoice generated on every successful payment (required by Israeli tax law)
- [ ] Required fields: business name, business number (ח.פ.), VAT number (עוסק מורשה), invoice number (sequential), date, customer name, service description, amount before VAT, VAT amount (17%), total
- [ ] VAT rate: 17% (current Israeli rate, configurable for future changes)
- [ ] Invoice number: sequential, gap-free, format: `CENA-2026-{6-digit sequential}`
- [ ] PDF generated with Hebrew RTL support, stored in S3: `s3://cena-invoices/{year}/{month}/{invoice-id}.pdf`
- [ ] Invoice downloadable via `GET /api/invoices/{invoiceId}` (authenticated, owner or admin only)
- [ ] Monthly invoice summary for school B2B accounts
- [ ] Invoice data sent to accounting system via webhook (Greeninvoice / Hashavshevet integration)

**Test:**
```csharp
[Fact]
public async Task InvoiceGenerator_CreatesValidIsraeliInvoice()
{
    var generator = new InvoiceGenerator(_config);
    var invoice = await generator.GenerateAsync(new InvoiceRequest
    {
        StudentId = "student-inv-1",
        CustomerName = "ישראל ישראלי",
        AmountBeforeVatCents = 4265, // ₪42.65 (₪49.90 / 1.17)
        VatAmountCents = 725,        // ₪7.25
        TotalAmountCents = 4990,     // ₪49.90
        Description = "Cena Premium - מנוי חודשי",
        PaymentMethod = "credit_card"
    });

    Assert.NotNull(invoice.InvoiceNumber);
    Assert.Matches(@"^CENA-2026-\d{6}$", invoice.InvoiceNumber);
    Assert.NotNull(invoice.PdfUrl);
    Assert.Equal(17m, invoice.VatPercentage);
    Assert.Equal(4990, invoice.TotalAmountCents);
}

[Fact]
public async Task InvoiceNumbers_AreSequential()
{
    var generator = new InvoiceGenerator(_config);
    var inv1 = await generator.GenerateAsync(CreateInvoiceRequest("student-seq-1"));
    var inv2 = await generator.GenerateAsync(CreateInvoiceRequest("student-seq-2"));

    var num1 = int.Parse(inv1.InvoiceNumber.Split('-').Last());
    var num2 = int.Parse(inv2.InvoiceNumber.Split('-').Last());
    Assert.Equal(num1 + 1, num2); // Sequential, gap-free
}

[Fact]
public async Task InvoiceDownload_RequiresAuth()
{
    var response = await _client.GetAsync("/api/invoices/CENA-2026-000001");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task InvoiceDownload_OnlyOwnerOrAdmin()
{
    // Student A tries to download Student B's invoice
    var token = GenerateTestFirebaseJwt(uid: "student-a", claims: new { role = "student" });
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await _client.GetAsync("/api/invoices/CENA-2026-000001"); // Belongs to student-b
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public void VatCalculation_17Percent()
{
    var rules = new IsraeliTaxRules();
    var result = rules.CalculateVat(totalIncludingVat: 4990);

    Assert.Equal(4265, result.AmountBeforeVat); // 4990 / 1.17 = 4264.96 ≈ 4265
    Assert.Equal(725, result.VatAmount);         // 4990 - 4265 = 725
    Assert.Equal(17m, result.VatPercentage);
}
```

---

## Integration Test (full payment lifecycle)

```csharp
[Fact]
public async Task FullPaymentLifecycle_TrialToPaymentToRefund()
{
    var studentId = "lifecycle-pay-1";

    // 1. Start trial
    var manager = new SubscriptionManager(_store, _firebaseAdmin);
    await manager.StartTrialAsync(studentId);
    Assert.Equal("premium", (await _firebaseAdmin.GetUserAsync(studentId)).CustomClaims["tier"]);

    // 2. Upgrade during trial (Stripe checkout)
    var session = await _stripeProvider.CreateCheckoutSessionAsync(new CheckoutRequest
    {
        StudentId = studentId, PlanId = "premium-monthly", AmountInCents = 4990, Currency = "ILS"
    });

    // 3. Simulate webhook: checkout completed
    await ProcessStripeWebhook("checkout.session.completed", new { client_reference_id = studentId });
    Assert.Equal(SubscriptionState.Active, (await manager.GetSubscriptionAsync(studentId)).State);

    // 4. Invoice generated
    var invoices = await _invoiceStore.GetInvoicesForStudentAsync(studentId);
    Assert.Single(invoices);
    Assert.Matches(@"^CENA-2026-\d{6}$", invoices[0].InvoiceNumber);

    // 5. Payment fails next month
    await ProcessStripeWebhook("invoice.payment_failed", new { customer = $"cus_{studentId}", attempt_count = 3 });
    Assert.Equal("free", (await _firebaseAdmin.GetUserAsync(studentId)).CustomClaims["tier"]);

    // 6. Refund
    await ProcessStripeWebhook("charge.refunded", new { customer = $"cus_{studentId}", amount = 4990 });
}
```

## Edge Cases
- Currency conversion race: ILS/USD rate changes between checkout and capture → use Stripe's built-in currency conversion
- PayPlus timeout during checkout → retry with same idempotency key, show "processing" to student
- Double webhook delivery → Redis idempotency key prevents double processing
- School admin cancels all seats → bulk tier downgrade via Firebase Admin SDK batch (max 1000 per batch)
- Invoice number gap from failed generation → log ERROR, manual reconciliation in accounting system

## Rollback Criteria
- If Stripe integration fails: accept manual bank transfers with admin-managed tier upgrades
- If PayPlus causes >5% payment failures: route all Israeli payments through Stripe
- If invoice generation fails: defer PDF generation to async queue, deliver within 24 hours

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes end-to-end
- [ ] `dotnet test --filter "Category=Payments"` → 0 failures
- [ ] Stripe test mode: successful charge, refund, subscription lifecycle
- [ ] PayPlus sandbox: successful charge with Israeli test card
- [ ] Israeli tax invoice: valid PDF with all required fields, sequential numbering
- [ ] No payment API keys in source code (verified via `gitleaks detect`)
- [ ] Webhook signature verification active on both providers
- [ ] PCI DSS compliance: no card numbers stored (delegated to Stripe/PayPlus)
- [ ] PR reviewed by architect (you)
