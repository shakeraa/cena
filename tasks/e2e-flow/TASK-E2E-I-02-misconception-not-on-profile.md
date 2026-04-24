# TASK-E2E-I-02: Misconception never attached to student profile (ADR-0003)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/misconception-not-on-profile.spec.ts`

## Journey

Student triggers a misconception via wrong answer → StudentProfile document fetched → contains NO misconception fields. Event-stream scan confirms no `MisconceptionEventV1` with `studentId` populated.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Profile | Shape contains zero misconception fields |
| Event-stream | `MisconceptionEventV1 WHERE studentId IS NOT NULL` → 0 rows |

## Regression this catches

Misconception data leaks to profile (ship blocker); event sourced with studentId (should be session_id only).

## Done when

- [ ] Spec lands
- [ ] Negative-property check in teardown
- [ ] Tagged `@ship-gate @p0`
