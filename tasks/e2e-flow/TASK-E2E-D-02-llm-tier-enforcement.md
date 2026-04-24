# TASK-E2E-D-02: LLM tier enforcement by subscription

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@llm @billing @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/llm-tier-enforcement.spec.ts`

## Journey

Basic-tier student → tutor request → routed to Haiku (tier-2). Plus-tier student → routed to Sonnet (tier-3). Observed via OTel trace tags.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| OTel trace | `llm.model` tag matches expected tier |
| Cost metric | `llm.cost_usd` scales proportionally |
| DOM | No tier-info leakage to user beyond their entitlement |

## Regression this catches

Router misses entitlement check (real-money cost leak); tier-3 routes to tier-7; Basic user getting Opus access.

## Done when

- [ ] Spec lands
- [ ] OTel collector assertion stable (tolerance band, not exact)
