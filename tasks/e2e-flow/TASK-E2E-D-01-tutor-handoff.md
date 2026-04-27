# TASK-E2E-D-01: Tutor handoff → AI chat → return to session

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@cas @llm @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/tutor-handoff.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Student in `/session/{id}` stuck → "Ask the tutor" → handoff to `/tutor/chat/{id}` → AI responds CAS-verified → back-navigate → session resumes with tutor-context preserved.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Chat UI renders; back-nav to session preserves question + tutor context |
| DB | `TutorSessionContext` linked to session |
| LLM | Correct tier routed for user's subscription |
| CAS | Every math step in AI response verified |
| Bus | `TutorHandoffInitiatedV1`, `TutorHandoffClosedV1` |

## Regression this catches

Tutor responds with un-verified math; session orphaned; wrong-tier routing.

## Done when

- [ ] Spec lands
- [ ] Tagged `@cas @p0`
