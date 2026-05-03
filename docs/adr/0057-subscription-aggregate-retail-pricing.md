# ADR-0057 — SubscriptionAggregate, retail pricing, and entitlement propagation

- **Status**: Accepted
- **Date**: 2026-04-22
- **Decision Makers**: Shaker (project owner), Architecture
- **Task**: [EPIC-PRR-I](../../tasks/pre-release-review/EPIC-PRR-I-subscription-pricing-model.md), [PRR-290..348](../../tasks/pre-release-review/)
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md), [ADR-0012](0012-aggregate-decomposition.md), [ADR-0026](0026-llm-three-tier-routing.md), [ADR-0038](0038-event-sourced-right-to-be-forgotten.md), [ADR-0041](0041-parent-auth-role-age-bands.md), [ADR-0042](0042-consent-aggregate-bounded-context.md), [ADR-0048](0048-exam-prep-time-framing.md), [ADR-0053](0053-external-integration-adapter-pattern.md)

---

## Context

EPIC-PRR-I introduces Cena's commercial retail SKU: a three-tier subscription (Basic ₪79 / Plus ₪229 decoy / Premium ₪249) with per-seat sibling discount, annual prepay, and a separate B2B school SKU at launch+1. Decisions locked by 10-persona pricing review 2026-04-22.

Before any code, three architectural questions need answers, because picking wrong locks in a multi-week debt:

1. **What aggregate owns the commercial relationship?** Parent? Student? Household? A new Billing context?
2. **How does tier entitlement reach the per-student session boundary?** Parent subscribes; student consumes. The enforcement seam is the LLM router + diagnostic intake (per-student), but the entitlement lives per-parent.
3. **How does retail pricing coexist with the existing `Cena.Actors.Pricing` namespace** (which serves B2B institute overrides per ADR-0051)? Is this one thing or two?

## Decisions

### 1. New bounded context — `Cena.Actors.Subscriptions`

Retail subscription is a new bounded context, NOT an extension of the existing `Pricing` namespace. Reasoning:

- `Cena.Actors.Pricing` handles **B2B institute-level pricing overrides** (`InstitutePricingResolver`, `InstitutePricingOverrideDocument`) — a content-authoring / institute-configuration concept tied to multi-institute tenancy (ADR-0051).
- `Cena.Actors.Subscriptions` handles **retail commercial contracts** — a parent-as-billing-counterparty concept tied to the commercial SKU.
- Collapsing the two would force the institute-override logic to know about Stripe, VAT, refund windows — none of which are institute concerns.
- Per ADR-0012 aggregate decomposition principles: separate lifecycles, orthogonal audit requirements, separate responsibility surface → separate bounded context.

### 2. Aggregate root — parent-keyed, event-sourced

**Stream key**: `subscription-{parentSubjectId}` — one stream per parent account.

Reasoning:

- The **parent is the billing counterparty** (the adult paying the card). Student accounts are **entitlement targets**, not billing entities. Israeli retail Bagrut market = parent pays, near-universally.
- Per-student streams would fragment the sibling-discount invariant (which is inherently a household-level rule: "second sibling ₪149, third+ ₪99").
- A separate "household" aggregate would be a fourth concept needing its own lifecycle — in this product the parent *is* the household billing identity. YAGNI.
- Per-parent keying matches ADR-0041 parent-auth semantics: `parent_of` claim already carries the student list this aggregate entitles.

**Events** (all V1, PII encrypted per ADR-0038):

- `SubscriptionActivated_V1` — parent + primary student + tier + cycle + payment txn
- `TierChanged_V1` — tier upgrade/downgrade with effective-at
- `BillingCycleChanged_V1` — monthly↔annual transition
- `SiblingEntitlementLinked_V1` — add sibling with applied discount
- `SiblingEntitlementUnlinked_V1` — remove sibling with pro-rata credit
- `RenewalProcessed_V1` — next-cycle payment cleared
- `PaymentFailed_V1` — past-due state, retry schedule
- `SubscriptionCancelled_V1` — active → cancelled
- `SubscriptionRefunded_V1` — money returned, state terminal
- `EntitlementSoftCapReached_V1` — per-student soft-cap telemetry

Per ADR-0038, parent subject id, primary/sibling student ids, and payment transaction ids are encrypted at the event boundary via `EncryptedFieldAccessor` and crypto-shredded on subject erasure.

### 3. Entitlement propagation — projection to per-student read model

The enforcement points (LLM router per ADR-0026, diagnostic upload cap per EPIC-PRR-J) need per-student lookups: "what tier does student X currently have, and what caps?"

The aggregate is parent-keyed. We project to a per-student read model:

```
StudentEntitlementView { studentSubjectId, effectiveTier, capsSnapshot, 
                        validUntil, sourceParentSubjectId }
```

Projection rule: every event on `subscription-{parentId}` is fanned out to one read-model update per student currently linked (primary + siblings). `TierChanged_V1` + `SiblingEntitlementLinked_V1` + `SubscriptionCancelled_V1` all trigger a rebuild of the affected students' views.

The session-pinning rule from PRR-310 reads `StudentEntitlementView` at session start and pins it into `SessionContext` for the session's lifetime. This keeps the LLM router, diagnostic cap, and feature-flag checks on a single source of truth and prevents mid-session entitlement oscillation.

### 4. Money representation — agorot-based long, no float

All money in the domain is represented as `long Agorot` (100 agorot = 1 ₪). Reasoning:

- Float/decimal rounding across tax math is a classical source of off-by-one customer-facing errors.
- All VAT computations are closed-form integer arithmetic.
- Serialization to Stripe (which expects smallest currency unit) is native.
- `Money` is a lightweight value record; conversions to/from display strings localized per locale.

### 5. VAT — pure function, gross-inclusive display, net stored alongside

Israeli consumer VAT is 17% and pricing is **displayed VAT-inclusive** (₪249 is what the parent pays). Storage convention:

- `GrossAgorot` = displayed price
- `NetAgorot` = `GrossAgorot * 100 / 117`, rounded to nearest agora
- `VatAgorot` = `GrossAgorot − NetAgorot` (guarantees exact reconciliation)

`IsraeliVatCalculator` is a pure static class with unit tests for boundary cases (odd-agora rounding, zero).

### 6. Tier catalog — compile-time constants, not database-sourced

For v1, tier definitions (price, caps, feature flags) live as **code constants** in `TierCatalog.cs`. Reasoning:

- Prices are launch-locked commercial decisions, not operator-tunable values.
- Changing a tier's caps mid-period has real contract implications — it should be a PR with legal review, not an admin-UI toggle.
- Per memory "No stubs — production grade": hardcoding real launch values is NOT a stub; it's the intended persistence layer for values that must not drift.
- Post-launch A/B experiments on pricing (PRR-332) will introduce an experiment-overlay layer that consults the catalog; the catalog remains the baseline.

Caps representation: `-1` = effectively unlimited (displayed as "unlimited" in UX, still bounded by soft-cap UX at a much higher threshold); positive integer = hard or soft cap depending on field.

### 7. Payment gateway — adapter pattern per ADR-0053

`IPaymentGateway` is the port; Stripe, Bit, PayBox implementations are adapters. Per ADR-0053 external-integration-adapter pattern. Webhook reception lives in `Cena.Student.Api.Host/Endpoints/` and translates gateway-specific signals into `SubscriptionCommands` calls. Gateway secrets via key vault only (per CLAUDE.md security rules).

### 8. School SKU and retail — shared aggregate, divergent constraints

The B2B school SKU (PRR-340) reuses `SubscriptionAggregate` with `SubscriptionTier.SchoolSku` and a school-tenancy discriminator. Feature-fencing (PRR-343) is enforced at the endpoint-authorization seam via `SkuFeatureAuthorizer`, not by having two aggregates. The aggregate's job is commercial-contract state; whether a feature is visible is a **read-side authorization** concern.

## Consequences

### Positive

- Parent-keyed stream matches billing and consent-parent semantics; no fourth aggregate.
- Entitlement projection gives per-student O(1) read at the enforcement seam, which keeps the LLM router hot-path simple.
- Agorot + pure VAT calculator makes the money-math unit-testable and stops float drift.
- Tier catalog as code means launch prices are in git history, PR-reviewed, impossible to silently mutate.
- Gateway adapter per ADR-0053 — Stripe/Bit/PayBox swappable without touching the aggregate.

### Negative

- `StudentEntitlementView` read-model is a new projection to maintain; rebuild time matters at scale. Mitigated by: each view is per-student, trivially partitioned, cheap to replay.
- The aggregate knows nothing about webhooks — webhook idempotency lives at the adapter + dedup-store layer. Two-layer discipline required.
- Sibling discount math lives partly in the aggregate (`SiblingEntitlementLinked_V1` carries the discount applied) and partly in the catalog (the rule that determines what the discount IS for sibling n). This split is intentional but demands a documented convention.

### Neutral

- Retail currency ILS-only at launch; multi-currency deferred to post-expansion (memory "PWA over Flutter" confirms Israel-first).
- No in-app purchases via Apple/Google at launch (PWA-direct-pay per memory) — no App Store 30% cut in the unit economics.

## Open items (tracked as §5 decisions on EPIC-PRR-I)

1. Price anchors final (₪79/229/249) or sensitivity-test pre-launch?
2. Plus decoy differentiator confirmed (unlimited photo diagnostic + Sonnet, no dashboard)?
3. Arabic parent dashboard on critical path (blocks Premium launch for Arabic-Israeli market per persona #6)?
4. B2B school SKU at launch or launch+1?
5. Bit + PayBox vendor DPAs — procurement owner?
6. Tutor-handoff PDF — Premium-included or ₪20 add-on?
7. Annual prepay depth (17% off vs shallower)?
8. EPIC-PRR-B governance ownership of per-tier LLM routing policy table?

## References

- [EPIC-PRR-I](../../tasks/pre-release-review/EPIC-PRR-I-subscription-pricing-model.md) — epic body and sub-task ladder
- [BUSINESS-MODEL-001-pricing-10-persona-review.md](../design/BUSINESS-MODEL-001-pricing-10-persona-review.md) — source review
- [ADR-0012](0012-aggregate-decomposition.md) — bounded-context decomposition principle
- [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) — PII event encryption + crypto-shred
- [ADR-0041](0041-parent-auth-role-age-bands.md) — parent auth semantics; subscription owner must be Adult role
- [ADR-0053](0053-external-integration-adapter-pattern.md) — payment-gateway adapter pattern
