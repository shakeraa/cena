# TASK-PRR-311: Per-tier LLM routing policy

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #4 student UX (Basic must not feel broken), #10 CFO (cost control)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (LLM routing layer)
**Tags**: epic=epic-prr-i, tier-enforcement, priority=p0, llm-routing, launch-blocker
**Status**: Ready (couples tightly to EPIC-PRR-B governance)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

LLM router honors per-tier policy: Basic = Haiku-first with 20 Sonnet escalations/week cap; Plus = Sonnet-for-complex unlimited; Premium = Sonnet-unlimited with soft cap at 100 escalations/mo.

## Scope

- Router reads `SubscriptionTier` + `UsageCaps` from `SessionContext` (PRR-310).
- Complexity estimate per call (existing ADR-0026 flow).
- Tier-aware decision:
  - Basic + low-complexity → Haiku
  - Basic + high-complexity + within cap → Sonnet
  - Basic + high-complexity + over cap → graceful degrade (friendly hint, not error)
  - Plus + high-complexity → Sonnet unlimited
  - Premium + any → Sonnet, soft-cap at 100/mo triggers upsell UX
- Rate-limit accounting per student per tier per calendar period.
- Coordination with [EPIC-PRR-B](EPIC-PRR-B-llm-routing-governance.md) — that epic owns the policy table; this task implements the consumer.

## Files

- `src/backend/Cena.LLM/Routing/TierAwareRouter.cs`
- `src/backend/Cena.LLM/Routing/RoutingPolicy.cs` (couples to PRR-B)
- Tests: per-tier decision matrix, cap-exceeded graceful degrade.

## Definition of Done

- Every tier decision covered by test.
- Cap-exceeded path returns usable answer (not error).
- Metrics: escalation rate per tier per week tracked.
- Full sln green.

## Non-negotiable references

- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md).
- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [EPIC-PRR-B](EPIC-PRR-B-llm-routing-governance.md)
- [PRR-310](TASK-PRR-310-subscription-tier-propagation.md)
