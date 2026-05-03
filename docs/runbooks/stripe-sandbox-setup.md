# Stripe sandbox — setup runbook (EPIC-PRR-I PRR-301)

Operator runbook for wiring the Stripe sandbox into Cena dev/test. Covers
API key provisioning, product/price catalog creation, webhook endpoint
registration, and local configuration. All values shown are examples —
replace with what Stripe's dashboard generates for your account.

## Prerequisites

- Stripe sandbox account (free; no KYC required for test-mode)
- `stripe` CLI installed locally (for webhook forwarding to `http://localhost:5050`)
- Access to the Cena repository and ability to set env variables

## Step 1 — API credentials

In the Stripe dashboard → Developers → API keys:

1. Copy **Secret key** (starts with `sk_test_...`) → save as
   `STRIPE_SECRET_KEY` env var or in the secret store used by your
   environment.
2. (Optional) Copy **Publishable key** (`pk_test_...`) — only required if
   Stripe.js is ever invoked in the PWA. Skippable at launch (we use
   hosted Checkout Sessions, no client-side Stripe.js).

Never commit these values. Secret scanning is in CI per CLAUDE.md security
rules.

## Step 2 — Product + price catalog

Create three **products** in the dashboard → Products → Add product:

| Product name   | Currency | Monthly recurring | Annual recurring |
|----------------|----------|-------------------|------------------|
| `Cena Basic`   | ILS      | ₪79.00            | ₪790.00          |
| `Cena Plus`    | ILS      | ₪229.00           | ₪2,290.00        |
| `Cena Premium` | ILS      | ₪249.00           | ₪2,490.00        |

For each price:

- Check **"Include tax in price"** (Israel VAT-inclusive display per
  ADR-0057 §5). Stripe will display the ₪249 gross amount to the parent
  and back out VAT for reporting.
- Billing period: `Monthly` or `Yearly` respectively.
- After creation, copy each **price ID** (`price_...`) — six total. These
  go into Cena configuration in Step 4.

## Step 3 — Webhook endpoint

Developers → Webhooks → Add endpoint:

- **Endpoint URL**: for production, `https://<student-api-host>/api/webhooks/stripe`.
  For local dev, use `stripe listen --forward-to localhost:5050/api/webhooks/stripe`
  which creates a forwarded endpoint automatically.
- **Events to send** (6):
  - `checkout.session.completed`
  - `customer.subscription.updated`  (optional — observability)
  - `customer.subscription.deleted`
  - `invoice.paid`
  - `invoice.payment_failed`
  - `charge.refunded`
- After creation, reveal and copy the **signing secret** (`whsec_...`) —
  save as `STRIPE_WEBHOOK_SECRET`. This secret is MANDATORY — the Cena
  webhook handler rejects every request without a valid signature.

## Step 4 — Cena configuration

Add a `Stripe` section to the appropriate `appsettings.{env}.json` or use
environment variables (preferred for secrets). Shape:

```json
{
  "Stripe": {
    "SecretKey": "",
    "WebhookSigningSecret": "",
    "PublishableKey": "",
    "PriceIds": {
      "BasicMonthly":   "price_xxx",
      "BasicAnnual":    "price_xxx",
      "PlusMonthly":    "price_xxx",
      "PlusAnnual":     "price_xxx",
      "PremiumMonthly": "price_xxx",
      "PremiumAnnual":  "price_xxx"
    },
    "SuccessUrl": "https://cena.test/subscription/confirm",
    "CancelUrl":  "https://cena.test/pricing"
  }
}
```

With env-var overrides:

```bash
export Stripe__SecretKey="sk_test_xxx"
export Stripe__WebhookSigningSecret="whsec_xxx"
export Stripe__PriceIds__BasicMonthly="price_xxx"
# … rest of the price IDs
```

The composition root checks `StripeOptions.IsConfigured` at startup. If any
required field is missing the app falls back to the `SandboxCheckoutSessionProvider`
(dev/test default) and logs a warning. In Production this fallback is a
misconfiguration — surface via the unit-economics dashboard's
"payment-gateway = sandbox in production" alert (PRR-330 follow-up).

## Step 5 — End-to-end smoke test

With credentials in place:

1. Start the Student API host (`dotnet run --project src/api/Cena.Student.Api.Host`
   or via the `cena-student-api` container).
2. Start the Stripe CLI in a second terminal:
   ```bash
   stripe listen --forward-to localhost:5050/api/webhooks/stripe
   ```
   Copy the printed signing secret into your local env as
   `Stripe__WebhookSigningSecret`.
3. Trigger a Checkout Session from the pricing page (or via curl):
   ```bash
   curl -X POST http://localhost:5050/api/me/subscription/checkout-session \
     -H "Authorization: Bearer <session-token>" \
     -H "Content-Type: application/json" \
     -d '{"primaryStudentId":"enc::student::1","tier":"Premium","billingCycle":"Monthly","idempotencyKey":"test-001"}'
   ```
   The response includes `checkoutUrl` — open it and complete payment with
   a Stripe test card (e.g., `4242 4242 4242 4242`, any future expiry, CVC 123).
4. Stripe fires `checkout.session.completed` → Cena webhook handler →
   `SubscriptionActivated_V1` appended to the parent's stream.
5. Verify with `GET /api/me/subscription` — status should be `Active`,
   tier `Premium`, renews in 1 month.

## Troubleshooting

- **`signature_verification_failed`** at the webhook — the `whsec_...`
  value doesn't match. Re-copy from the dashboard; if using `stripe listen`,
  the CLI-issued secret overrides the dashboard one for local dev.
- **404 on `/api/webhooks/stripe`** — the Stripe handler is not registered
  because `StripeOptions.IsConfigured` returned false at startup. Check
  that all 6 price IDs + both secrets are set.
- **API version mismatch** — the Stripe SDK (v47.4.0) expects
  `2025-02-24.acacia`. If your dashboard webhook is configured for a
  different API version, update the webhook endpoint's API version in the
  dashboard.

## Production rotation

Rotate `sk_test_...` → `sk_live_...` by swapping the env var. The
composition root refuses to register a `sk_live_...` key in a non-Production
environment (safeguard). Webhook signing secret rotates independently —
create a new webhook endpoint with the new secret, let both run in parallel
for 24h, then delete the old one.
