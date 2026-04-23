# TASK-E2E-H-01: Student of institute A cannot read institute B's questions

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cross-tenant-questions.spec.ts`

## Journey

Student in institute A triggers session → backend picks questions filtered by A's tenant → B's pre-seeded questions never surface.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB query log | Emitted SQL contains `tenant_id = 'A'` predicate |
| Session | B's question ids never appear in A's session |

## Regression this catches

QuestionBank forgot tenant filter → cross-institute content served.

## Done when

- [ ] Spec lands
- [ ] Run both A→B and B→A (asymmetric regressions common)
- [ ] Tagged `@tenant @p0`
