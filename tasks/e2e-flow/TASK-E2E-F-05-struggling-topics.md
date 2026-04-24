# TASK-E2E-F-05: Struggling-topic surface (prr-049)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-F](EPIC-E2E-F-teacher-classroom.md)
**Tag**: `@teacher @ship-gate @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/struggling-topics.spec.ts`

## Journey

Teacher dashboard shows "Topics where ≥3 students struggled this week" — topic, student count, suggested intervention.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | Projection refreshes ≤ 1h async |
| DOM | Actionable numbers shown; NO banned engagement copy (streak etc) |
| i18n | Topic names localized (en/ar/he) |

## Regression this catches

Banned copy slips in ("15-day streak!"); wrong denominator includes inactive students; topic names not localized.

## Done when

- [ ] Spec lands
- [ ] Ship-gate regex scanner runs inline
