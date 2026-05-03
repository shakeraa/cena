# TASK-E2E-C-07: Socratic explain-it-back (prr-074 / F1)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @llm @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/explain-it-back.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Student selects "Explain it back to me" on a solved problem → LLM prompts for student's own words → student types → CAS-verified match to expected concept → mastery +boost.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Explain-it-back UI renders |
| LLM payload (test-mode recorder) | No PII per ADR-0047 |
| CAS | Verification path re-used for explanation match |
| DB | Mastery boost recorded via `ExplainItBackCompletedV1` |

## Regression this catches

PII in LLM prompt (ADR-0047 violation); explanation feedback hallucinating; mastery boost not recorded.

## Done when

- [ ] Spec lands
- [ ] LLM recorder shared with D-05 (don't duplicate)
