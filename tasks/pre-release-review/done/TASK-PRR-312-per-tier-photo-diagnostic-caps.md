# TASK-PRR-312: Per-tier photo diagnostic caps

**Priority**: P0 — launch-blocker
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO (abuse control), #4 student (graceful block)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (couples to EPIC-PRR-J)
**Tags**: epic=epic-prr-i, tier-enforcement, priority=p0, diagnostic-cap
**Status**: Ready (dependency: EPIC-PRR-J PRR-400 implements the counter)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Enforce per-tier photo-diagnostic caps: Basic = 0 (upgrade prompt shown), Plus = 20/mo, Premium = 100/mo soft + 300/mo hard. Caps defined here; counter + UX live in EPIC-PRR-J.

## Scope

- Define caps in tier data ([PRR-291](TASK-PRR-291-tier-feature-matrix-data.md)).
- Coordinate with [PRR-400](TASK-PRR-400-per-tier-upload-counter.md) for counting + enforcement.
- Basic-tier upload attempt → redirect to upgrade pricing page.
- Plus soft cap = hard cap (20/mo). Overage = "upgrade for more" or "wait until next month".
- Premium soft 100 = graceful upsell; hard 300 = "contact support" for legitimate exam-cram.

## Files

- `src/backend/Cena.Domain/Subscriptions/DiagnosticCaps.cs`
- Tests with [PRR-400](TASK-PRR-400-per-tier-upload-counter.md).

## Definition of Done

- Caps defined in tier seed data.
- Enforcement integration test green end-to-end with EPIC-PRR-J counter.
- Full sln green.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — upsell UX positive framing.
- [`docs/engineering/shipgate.md`](../../docs/engineering/shipgate.md) — no scarcity.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-291](TASK-PRR-291-tier-feature-matrix-data.md), [PRR-400](TASK-PRR-400-per-tier-upload-counter.md)
- [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)
