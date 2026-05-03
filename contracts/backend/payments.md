# Cena Platform — Subscription & Payment Integration Contract

**Layer:** Backend / Billing | **Runtime:** .NET 9
**Providers:** Stripe (international), PayPlus (Israeli market)
**Status:** BLOCKER — no payment integration defined; monetization path blocked

---

## 1. Payment Providers

| Provider | Market | Currency | Use Case |
|----------|--------|----------|----------|
| Stripe | International | USD, EUR | Credit/debit cards, international parents |
| PayPlus | Israel | ILS (NIS) | Israeli credit cards, shekel processing, Bit payments |

### Provider Selection Logic

1. If billing address is in Israel -> PayPlus (preferred for local card processing).
2. If billing address is outside Israel -> Stripe.
3. Parent can override provider selection in settings.

---

## 2. Subscription Plans

| Plan | Price | Billing | Features |
|------|-------|---------|----------|
| Free | 0 NIS | - | 2 concepts/day, basic Socratic tutoring, no spaced repetition outreach |
| Premium Monthly | 89 NIS/month | Monthly recurring | Unlimited concepts, all methodologies, WhatsApp outreach, analytics |
| Premium Annual | 799 NIS/year | Annual recurring | Same as Premium Monthly (save 252 NIS/year) |

### Free Tier Limits

| Resource | Free Limit | Premium Limit |
|----------|-----------|---------------|
| Concepts per day | 2 | Unlimited |
| LLM token budget | 5,000 output tokens/day | 25,000 output tokens/day |
| Methodologies | Socratic only | All 8 MCM methodologies |
| WhatsApp outreach | None | Full spaced repetition notifications |
| Parent analytics | Basic (mastery %) | Full dashboard with session details |
| Knowledge graph depth | 1 level deep | Full graph traversal |

---

## 3. Trial Period

| Parameter | Value |
|-----------|-------|
| Duration | 7 days |
| Credit card required | No |
| Auto-convert | No (explicit upgrade required) |
| Trial features | Full Premium access |
| Trial ending notification | 2 days before, 1 day before, day of expiry |
| Post-trial | Downgrade to Free tier automatically |

### Trial Flow

1. New parent account created -> 7-day Premium trial starts automatically.
2. Day 5: Push notification + WhatsApp template "Your trial ends in 2 days."
3. Day 6: Push notification "Last day of unlimited access tomorrow."
4. Day 7: Trial expires at 23:59 IST.
5. Day 8: Account downgraded to Free. Prompt to upgrade on next app open.

---

## 4. Payment Model: Parent Pays

- The **parent** account holds the subscription and payment method.
- A parent can link 1-5 student accounts under one subscription.
- Premium access is granted to **all linked students** under one payment.
- Teacher accounts are free (school licenses handled separately, future phase).

### Account Linking

```
Parent Account (billing owner)
  ├── Student 1 (child) — inherits parent's plan
  ├── Student 2 (child) — inherits parent's plan
  └── Student 3 (child) — inherits parent's plan
```

---

## 5. Webhook Events

### Stripe Webhooks

| Event | Action |
|-------|--------|
| `checkout.session.completed` | Activate Premium for parent + all linked students |
| `invoice.payment_succeeded` | Log payment, extend subscription period |
| `invoice.payment_failed` | Start dunning flow (see section 6) |
| `customer.subscription.deleted` | Downgrade to Free tier |
| `customer.subscription.trial_will_end` | Send trial-ending notification (3 days before) |
| `charge.refunded` | Process refund, maintain access until period end |

### PayPlus Webhooks

| Event | Action |
|-------|--------|
| `payment.success` | Activate Premium |
| `payment.failure` | Start dunning flow |
| `subscription.cancelled` | Downgrade to Free tier |
| `refund.processed` | Log refund |

### Webhook Security

- Stripe: Verify `Stripe-Signature` header using webhook signing secret.
- PayPlus: Verify HMAC-SHA256 signature using PayPlus secret key.
- All webhooks processed idempotently (deduplicate by event ID).
- Failed webhook processing: retry from provider (Stripe retries for 72h).

---

## 6. Dunning (Failed Payment Recovery)

| Step | Timing | Action |
|------|--------|--------|
| 1st retry | +1 day | Automatic retry by Stripe/PayPlus |
| 2nd retry | +3 days | Automatic retry + push notification to parent |
| 3rd retry | +7 days | Automatic retry + WhatsApp message + email |
| Downgrade | +10 days | Downgrade to Free tier, notify parent |

### Grace Period

- During dunning (days 1-10): maintain Premium access (grace period).
- After downgrade: parent can re-upgrade immediately; no data is lost.
- Student progress, mastery maps, and session history are preserved on Free tier.

---

## 7. Israeli Tax Compliance

### Tax Invoice (חשבונית מס)

| Field | Value |
|-------|-------|
| VAT rate | 18% (current Israeli rate, configurable) |
| Invoice type | חשבונית מס (tax invoice) for Premium subscriptions |
| Invoice number | Sequential, per Israeli tax authority requirements |
| Business details | Cena Ltd., Israeli business number (ח.פ.) |
| Currency | NIS for PayPlus, USD for Stripe (dual display) |

### Invoice Generation

1. On `payment_succeeded`: generate PDF invoice with Hebrew text.
2. Store invoice in S3 bucket with 7-year retention (Israeli tax law).
3. Make available in parent dashboard under "Invoices" (חשבוניות).
4. Email invoice PDF to parent's registered email.

### Receipt Fields

```
חשבונית מס מספר: INV-2026-00123
תאריך: 26/03/2026
לכבוד: {parent_name}
פרטי עסקה: מנוי פרמיום חודשי — צנ"א
סכום לפני מע"מ: 75.42 ₪
מע"מ (18%): 13.58 ₪
סה"כ: 89.00 ₪
```

---

## 8. Refund Policy

| Rule | Detail |
|------|--------|
| Guarantee | 14-day money-back guarantee (Israeli Consumer Protection Law 5741-1981) |
| Refund window | 14 days from first payment (not trial start) |
| Refund method | Same payment method as original charge |
| Partial month | Pro-rated refund for annual plans within 14 days |
| After 14 days | No refund; subscription continues until period end |
| Cancellation | Effective at end of current billing period |

### Refund Flow

1. Parent requests refund via app settings or support email.
2. System validates: within 14-day window, not previously refunded.
3. Process refund via Stripe/PayPlus API.
4. Maintain Premium access until end of current billing period.
5. Generate credit note (חשבונית זיכוי) for tax compliance.

---

## 9. Data Model

### Subscription Entity

```
Subscription {
  id: uuid
  parent_id: uuid (FK -> Parent)
  provider: "stripe" | "payplus"
  provider_subscription_id: string
  plan: "free" | "premium_monthly" | "premium_annual"
  status: "trialing" | "active" | "past_due" | "cancelled" | "expired"
  current_period_start: datetime
  current_period_end: datetime
  trial_end: datetime?
  cancelled_at: datetime?
  dunning_step: 0-3
  created_at: datetime
  updated_at: datetime
}
```

### Payment Event (Event Sourced)

```
PaymentEvent {
  event_id: uuid
  subscription_id: uuid
  type: "payment_succeeded" | "payment_failed" | "refunded" | "plan_changed"
  amount_nis: decimal
  vat_amount_nis: decimal
  provider_event_id: string
  invoice_number: string?
  occurred_at: datetime
}
```

---

## 10. Monitoring

| Metric | Alert Threshold |
|--------|-----------------|
| Payment failure rate | > 5% in 1 hour |
| Webhook processing lag | > 30 seconds |
| Dunning conversion rate | < 50% recovery (weekly) |
| Trial-to-paid conversion | < 10% (weekly, business alert) |
| Refund rate | > 15% (monthly, business alert) |
