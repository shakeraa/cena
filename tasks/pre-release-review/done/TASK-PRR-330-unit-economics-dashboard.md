# TASK-PRR-330: Unit-economics dashboard (per-tier contribution margin)

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #10 CFO
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + data-eng + finance
**Tags**: epic=epic-prr-i, observability, commercial, priority=p0
**Status**: Backend-done (admin Vue page + Slack/email adapter deferred)
**Branch**: `claude-subagent-prr330/prr-330-unit-economics`
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Weekly automated dashboard: per-tier ARPU, COGS, contribution margin, LTV, churn, CAC payback. Memory "Honest not complimentary" — real numbers with CIs, not aspirational.

## Scope

- Pulls: subscription events, LLM usage cost, OCR cost, infra cost, CAC from marketing attribution.
- Metrics per tier: gross revenue, net-after-VAT, COGS (LLM + OCR + infra allocated), contribution margin $/mo, LTV, churn %.
- Weekly Slack/email summary to decision-holder.
- Alert when Premium contribution <$20/mo for 2 consecutive weeks.
- Honest-numbers framing: CIs on LTV, not point estimates.

## Files

- `src/admin/full-version/src/pages/finance/unit-economics.vue`
- `src/backend/Cena.AdminApi/Controllers/UnitEconomicsController.cs`
- `src/backend/Cena.StudentApi/Workers/UnitEconomicsRollupWorker.cs`
- Tests.

## Definition of Done

- Dashboard shows current-week per-tier numbers.
- Weekly summary delivers.
- Alert fires on margin compression.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary" — CIs mandatory on LTV.
- Memory "Verify data E2E" — numbers match actual DB.

## Reporting

complete via: standard queue complete.

## Related

- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Backend delivery summary (2026-04-23)

Delivered on branch `claude-subagent-prr330/prr-330-unit-economics` (3 commits):

1. `src/actors/Cena.Actors/Subscriptions/UnitEconomicsSnapshotDocument.cs` — Marten doc keyed by `week-YYYY-MM-DD`, pure Sunday-snap helpers.
2. `src/actors/Cena.Actors/Subscriptions/IUnitEconomicsSnapshotStore.cs` + `InMemoryUnitEconomicsSnapshotStore.cs` + `MartenUnitEconomicsSnapshotStore.cs` — Upsert / ListRecent / Get. InMemory is production-grade for single-host; Marten swaps in under `AddSubscriptionsMarten`.
3. `src/actors/Cena.Actors/Subscriptions/UnitEconomicsRollupWorker.cs` — Sunday 06:00 UTC `BackgroundService`. Idempotent (skip if week already computed), fires `unit_economics_margin_compression` warning after 2 consecutive weeks of Premium NET revenue per active below threshold (default 2000 agorot, configurable via `Cena:UnitEconomics:Rollup`).
4. `src/actors/Cena.Actors/Subscriptions/UnitEconomicsAggregationService.cs` — extracted `IUnitEconomicsAggregationService` seam so the worker and the admin endpoint can share a test-replaceable dependency.
5. `src/api/Cena.Admin.Api.Host/Endpoints/UnitEconomicsAdminEndpoints.cs` — `GET /api/admin/unit-economics/history?weeks=12`, `AdminOnly` policy, clamps at 52. Wired in `Cena.Admin.Api.Host/Program.cs`.
6. `src/actors/Cena.Actors/Subscriptions/SubscriptionMartenRegistration.cs` — added `UnitEconomicsSnapshotDocument` schema.
7. `src/actors/Cena.Actors/Subscriptions/SubscriptionServiceRegistration.cs` — InMemory store + aggregator registered under `AddSharedServices`; Marten store + hosted worker + options wired under `AddSubscriptionsMarten`.

Tests (28 new / 42 total under `UnitEconomics*`):
- Week-id format + snap-to-Sunday (incl. non-UTC offset inputs)
- Store round-trip, idempotent overwrite, list ordering + clamp
- Sunday 06:00 UTC scheduling (Monday / Sat 23:59 / Sun 05:00 / Sun 07:00)
- Never-negative `TimeUntilNextSundayMorning` floor, non-UTC snap
- Idempotent second run does NOT recompute
- Margin-compression alert: fires only on 2 consecutive bad weeks
- Alert respects configured threshold
- End-to-end alert with seeded prior week + below-threshold computed current
- `PremiumAgorotPerActive` zero-active safety + refund-subtraction correctness
- Admin endpoint clamp + newest-first + empty-store + wire-DTO projection

Full `Cena.Actors.sln` build: 0 errors.

### Deferred (out of backend scope)

1. **Admin Vue page** at `src/admin/full-version/src/pages/finance/unit-economics.vue` — history endpoint + DTOs are the contract it reads against.
2. **Slack / email adapter** consuming the `unit_economics_margin_compression` log tag — the structured log IS the durable ops seam (Serilog → Loki). Adapter is a routing adapter, not a new source of truth.
3. **CIs / LTV / churn % / CAC payback**: the snapshot primitives (raw counts + gross + refunds, honest-not-complimentary) are the foundation; the statistical layer that turns them into CIs lives in a follow-up because it needs the attribution-refined projection (noted in `UnitEconomicsCalculator.cs` v1 comments) — cross-tier renewal/cancel attribution is the blocker for honest per-tier LTV.
4. **COGS ingestion** (LLM + OCR + infra allocated per tier) — the snapshot shape already carries revenue primitives; COGS joins in the statistical layer above and feeds contribution-margin = (net revenue − COGS)/active.
