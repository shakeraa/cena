# Stripe Radar — SetupIntent Toggle Runbook

**Status:** REQUIRED before Phase 1C SetupIntent provider task ships to production.
**Ownership:** Cena platform / billing operator.
**Last reviewed:** 2026-04-28 (Phase 1B fingerprint-ledger merge).
**Related:**
- [docs/design/trial-then-paywall-001-discussion.md §4.0 + §5.7](../design/trial-then-paywall-001-discussion.md)
- [docs/design/trial-recycle-defense-001-research.md §1.3 + §2.2 + §3.1 + §3.4](../design/trial-recycle-defense-001-research.md)
- ADR-0057 retail subscription bounded context.

---

## Why this matters

The Phase 1B trial fingerprint ledger (`TrialFingerprintLedger`) implements
the L3a layer of the four-layer recycle defense per
[trial-recycle-defense-001-research.md §2.2](../design/trial-recycle-defense-001-research.md).
L3a alone closes ~80-85% of recycle attacks. The remaining closure comes
from L1 (basic Stripe Radar) and L3b (Radar Fraud Teams), which fire ONLY
when the dashboard toggle described below is on.

**Default Stripe behaviour:** Radar evaluates rules on PaymentIntent
charges out of the box, but it does NOT evaluate them on SetupIntent
confirmations unless an operator explicitly opts in. Cena's trial-start
flow uses SetupIntents (no charge until conversion), so without the
toggle Radar gives Cena nothing on the trial path.

If the toggle is OFF when Phase 1C (SetupIntent provider) ships, the
defense degrades silently to L2 + L3a only — closure drops from
~85-95% to ~75-85% of attacks per the research brief's measured layers.
The trial-start endpoint will work, money won't be lost, but the abuse-
deterrent surface is materially weaker than what the design specifies.

---

## The toggle (one-click)

Path: **Stripe Dashboard → Settings → Radar → "Use Radar on payment
methods saved for future use" → ON**.

Stripe docs reference: [Stripe Radar overview — SetupIntents](https://docs.stripe.com/radar) and [Stripe Radar lists](https://docs.stripe.com/radar/lists).

### Procedure

1. Log in to the Stripe Dashboard with an operator account that has
   Radar admin permissions.
2. Open **Settings → Radar** (or navigate directly to
   `https://dashboard.stripe.com/settings/radar`).
3. Locate **"Use Radar on payment methods saved for future use"**.
4. Toggle to **ON**.
5. Save.
6. Verify by initiating a single sandbox SetupIntent confirmation
   (`stripe trigger setup_intent.succeeded` via the Stripe CLI) and
   observing a Radar review entry in the Dashboard's
   **Payments → All payments → Reviews** tab — SetupIntents only
   surface there once the toggle is on.

### Verification per environment

Each Stripe account / livemode is independent. Repeat the toggle for:

| Environment | Stripe Mode | Owner | Frequency |
|---|---|---|---|
| Sandbox / dev (test keys) | testmode | Backend lead | Once at first onboarding |
| Production (live keys) | livemode | Platform owner | Once before Phase 1C ship; verify quarterly |
| Staging (if separate Stripe account) | testmode | Backend lead | Once at first onboarding |

The toggle is **per-Stripe-account, not per-API-key**, so flipping it in
testmode does NOT propagate to livemode. Operators must check both.

### How to confirm it's enabled

Three independent checks — use any one or all:

1. **Dashboard read-back:** the toggle UI itself shows the state.
2. **Stripe CLI:** `stripe customers retrieve <customer-id>` does not
   reveal the toggle state directly, but a SetupIntent created against
   a known-bad test card (`4000000000009995`, "always declines —
   stolen card") should produce a `risk_level: "elevated"` field on
   the SetupIntent JSON when the toggle is on. Compare against the
   same call before flipping the toggle (no `risk_level` returned).
3. **Webhook log:** with the toggle on, `setup_intent.succeeded`
   webhooks include `radar_options` and a Radar `review` resource;
   without the toggle, those fields are absent.

---

## Pre-launch verification email to Stripe (Q-R2-followup)

Per [trial-recycle-defense-001-research.md §3.1 item #1](../design/trial-recycle-defense-001-research.md), there is one
open question that the dashboard toggle alone does NOT answer: **does
the trial-abuse-specific 90%-accuracy ML model fire on SetupIntent
scans, or only on PaymentIntent charges?** The basic Radar rules engine
fires either way, but the dedicated trial-abuse model is a Stripe-trained
classifier and Stripe's public docs do not explicitly confirm which
intent types it evaluates.

**Required action:** before Phase 1C ships, Cena's backend lead emails
`trial-abuse-prevention@stripe.com` (or files a Dashboard support ticket)
with the script below. The answer determines whether companion-brief
§3.2 Edit 4 needs the "fallback: create a $0 Subscription" sub-bullet.

### Verification email script (copy-paste, verbatim from §3.4)

> **Subject:** Radar trial-abuse model — does it fire on SetupIntents?
>
> Hi Stripe team,
>
> We're shipping a 14-day free trial flow where we collect a payment
> method via SetupIntent (off-session usage) at trial start. We OWN the
> trial timer ourselves and only create a Stripe Subscription on
> conversion (to avoid auto-charge per Israeli ePrivacy auto-renewal
> interpretation).
>
> Two questions:
>
> 1. Confirmed in your docs that Radar evaluates rules on SetupIntents
>    when "Use Radar on payment methods saved for future use" is enabled.
>    Does the **trial-abuse-specific ML model** (the one with the
>    90%-accuracy claim from your blog) also fire on these SetupIntent
>    scans? Or only on PaymentIntent charge attempts?
>
> 2. If trial-abuse model is PaymentIntent-only: would creating a Stripe
>    Subscription at trial-start with `trial_period_days: 0` (so the
>    first invoice is immediate, $0 amount, just to trigger Radar
>    scoring) be the recommended pattern? Any caveats?
>
> Our scale: ~1000 trial-starts/month at pilot, growing to ~10k/month
> over 6 months. We're already on Stripe Standard pricing and willing to
> upgrade to Radar for Fraud Teams.
>
> Cena Platform — [contact details]

**SLA expectation:** Stripe support typically responds in 2-5 business
days for non-urgent questions. Block Phase 1C ship until the answer
arrives; document the response in this runbook (append a "Stripe
response received" section below).

---

## Stripe response received

> **TODO** — replace this section with the actual Stripe answer once
> received. Include the date, the Stripe contact name, and the
> verbatim quote from their reply. If their answer is "PaymentIntent
> only", file a follow-up Phase 1C task to add the
> `trial_period_days: 0` Subscription pattern per the §3.4 fallback.

---

## When NOT to flip the toggle

The toggle has one known side-effect: it makes SetupIntent confirmations
score-able by Radar, which in rare cases can cause a legitimate
SetupIntent to be flagged for manual review. In v1 Cena DOES want this
behaviour — the alternative is silently letting recycle attempts through.
The mitigation is the customer-support escalation path documented in the
companion brief §5.7 ("contact-support" option in the 409 response body):
operator can manually approve a flagged SetupIntent in the Stripe
Dashboard's review queue and then re-issue trial-start.

The cost of this rare false-positive is well below the cost of disabling
the L1 layer entirely. Do NOT toggle off unless instructed by counsel
or in response to a measured >5% false-positive rate post-launch.

---

## Quarterly audit checklist

Once per quarter (rotate with the Israeli VAT receipt audit), verify:

1. The toggle is still ON in livemode (Settings → Radar UI).
2. The Stripe CLI smoke test (above) returns `risk_level` on a sandbox
   SetupIntent.
3. Recent SetupIntent succeeded webhooks include `radar_options`.
4. The L3a fingerprint ledger has no rows older than the trial-recycle
   statute of limitations Cena documents in its privacy policy
   (currently 7 years per Israeli PPL fraud-claim retention, per
   research brief §1.5a). Older rows can be vacuumed via a separate
   admin script (out of scope for this runbook).

If any of (1)-(3) fail, escalate to the platform owner immediately —
Cena's recycle-defense closure rate has degraded silently.
