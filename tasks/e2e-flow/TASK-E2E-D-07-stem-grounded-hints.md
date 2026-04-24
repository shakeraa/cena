# TASK-E2E-D-07: Stem-grounded hints (PRR-262)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@cas @compliance @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/stem-grounded-hints.spec.ts`

## Journey

Student requests a hint → hint generator returns stem-grounded text using ONLY the question's stem + student's prior attempt → CAS-verified.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| LLM payload (recorder) | Prompt contains stem + attempt only; NO cross-session context |
| DB | `HintGeneratedV1` with `source=stem-grounded` |
| CAS | Hint math verified before surfacing |

## Regression this catches

Hint pulling from other students' sessions (ADR-0003 violation); ungrounded hint (nonsense math); cross-tenant context leak.

## Done when

- [ ] Spec lands
- [ ] Negative-property: scan prompt for "session_id" / "student_id" from a different session → zero matches
