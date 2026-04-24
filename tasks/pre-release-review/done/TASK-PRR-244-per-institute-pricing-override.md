# TASK-PRR-244: Per-institute pricing override (super-admin configurable)

**Priority**: P1
**Effort**: M (1 week — 3 surfaces: admin UI + resolver + audit)
**Lens consensus**: persona-finops, persona-privacy, persona-enterprise
**Source**: User decision 2026-04-21 on Q5 of `docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md §14.6`
**Assignee hint**: backend-dev + coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, pricing, tenant-config
**Status**: Blocked on PRR-217 (ADR records the default constants + override policy)
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Super-admins (SUPER_ADMIN role, not institute admin) can override the
SaaS-global default pricing on a per-institute basis for: student monthly
price, institutional per-seat price (with minimum-seat threshold), and
free-tier monthly session cap. Every override is audit-logged with
justification text and surfaces in both billing and finance dashboards.

## Context

User decision 2026-04-21 locked default pricing at $19/month student tier,
$14/seat institutional (≥20 seats), 10-session/month free tier with
tier-2-only LLM. Use cases for overrides: subsidized regional rollout,
enterprise school-network discount, free-tier expansion for underserved
cohorts, pilot-program promotional pricing.

## Scope

### 1. Data model

New document / event stream under `Cena.Actors.Pricing`:

- `InstitutePricingOverrideDocument` — one per institute with override.
  Fields: `InstituteId`, `StudentMonthlyPriceUsd`, `InstitutionalPerSeatPriceUsd`, `MinSeatsForInstitutional`, `FreeTierSessionCap`, `EffectiveFromUtc`, `EffectiveUntilUtc?`, `OverriddenBySuperAdminId`, `JustificationText`, `CreatedAtUtc`.
- `InstitutePricingOverridden_V1` event on every write (append-only; the
  document is a projection).
- Defaults live in `contracts/pricing/default-pricing.yml` — version-pinned.
  ADR-0050 (already accepted) records the default values as of 2026-04-21.

### 2. Resolver

- `IInstitutePricingResolver.ResolveAsync(instituteId, CancellationToken)`
  returns a `ResolvedPricing` record: `{ studentMonthlyPriceUsd,
  institutionalPerSeatPriceUsd, minSeatsForInstitutional,
  freeTierSessionCap, source: "default" | "override" }`.
- Resolver reads the override doc if present, else returns defaults from
  the YAML. Cached in Redis with 5-minute TTL (short enough that an override
  takes effect promptly without hammering Marten).
- Every pricing-bearing code path in the codebase MUST call the resolver —
  no hard-coded dollar amounts. Architecture ratchet
  `NoHardcodedPricingTest` scans `src/**/*.cs` for `\$?\d+(\.\d{2})?` dollar
  literals under pricing-adjacent namespaces and fails CI on unregistered
  literals.

### 3. Admin UI (SUPER_ADMIN-role-gated)

- New route `/admin/institutes/:id/pricing-override` under the admin app.
- Role gate: only SUPER_ADMIN; institute admins see a read-only view
  ("Your institute's pricing — contact Cena for adjustments").
- Form: three numeric fields + justification textarea (required, min 20
  chars) + "Effective from" datepicker (default today).
- Validation: student price ≥ $3.30 floor (cost ceiling + infra); per-seat
  price ≥ $3.30 × 0.7 = $2.31 (min 30% margin below student price on
  institutional); free-tier cap in [0, 500] range. Values outside the
  reasonable range require a secondary "override warning" confirm dialog
  with the text of the decision rationale.
- Display: current override status + full history (every prior override
  with diff vs defaults + justifications + actor + timestamp).

### 4. Audit

- `InstitutePricingOverridden_V1` event + SIEM log tag
  `pricing.override.applied`.
- Log fields: `super_admin_id`, `institute_id`, `old_student_price`,
  `new_student_price`, `old_per_seat_price`, `new_per_seat_price`,
  `old_free_tier_cap`, `new_free_tier_cap`, `justification_text`,
  `effective_from`, `trace_id`.
- Finance dashboard filter: "show institutes with active pricing override".

### 5. Billing integration

- Billing job reads from `IInstitutePricingResolver.ResolveAsync` when
  generating invoices — no direct config read. Ensures overrides apply
  automatically without billing code changes.
- Stripe metadata (when we get there) carries the resolved pricing values
  + `pricing.source = "default" | "override"` so Stripe reports reflect
  the actual charged amount.

## Non-negotiables

- No files >500 LOC.
- No hard-coded dollar literals anywhere outside the YAML defaults file
  and the resolver itself. Arch ratchet enforces.
- No override silently applied — every change goes through the
  SUPER_ADMIN-gated form with justification + audit trail.
- ADR-0001 tenant scoping preserved (an institute admin must never see
  another institute's override).
- Privacy: `justification_text` may contain business-sensitive context;
  it's visible only to SUPER_ADMIN + finance roles, never to students
  or institute admins.
- Ship-gate scanner green on new copy (no FOMO / scarcity / loss-aversion
  in the admin form copy).

## Senior-architect protocol

Ask *why* the default pricing exists at all if it's overridable. Answer:
defaults provide the baseline for SaaS-wide reporting + auto-billing for
new institutes (who get defaults until an override is applied). Overrides
are the exception, not the rule — finance dashboards surface overrides
prominently so the default is the truth for 95%+ of institutes.

## Tests

- Unit: resolver falls back to YAML defaults when no override exists.
- Unit: resolver returns override values when an override is present.
- Integration: POST `/admin/institutes/{id}/pricing-override` with valid
  payload → override persisted + event emitted + audit log tagged.
- Integration: institute admin attempting the same endpoint → 403.
- Integration: cross-institute attempt (SUPER_ADMIN at tenant A editing
  institute B) → explicit tenant check (SUPER_ADMIN can cross, regular
  admin cannot).
- Arch: `NoHardcodedPricingTest` fails on a crafted fixture with a
  literal $19 in a pricing file.

## Reporting

```sh
git add -A
git commit -m "feat(prr-244): per-institute pricing override — SUPER_ADMIN-gated + audit trail + arch ratchet"
git push
```
