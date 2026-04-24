# TASK-E2E-L-03: Parent digest email rendered RTL

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@i18n @parent @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/digest-rtl.spec.ts`

## Journey

Parent locale = `he` → weekly digest triggers → email received → HTML has `dir="rtl"` + `html lang="he"` → child's name correct → math excerpt inside `<bdi dir="ltr">`.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Email HTML source | Captured by test SMTP sink |
| DOM snapshot | Direction + language correct |

## Regression this catches

LTR template used for RTL audience; mixed-direction content not isolated.

## Done when

- [ ] Spec lands
