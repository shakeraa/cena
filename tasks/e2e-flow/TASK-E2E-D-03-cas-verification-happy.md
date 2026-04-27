# TASK-E2E-D-03: CAS verification — happy path

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@cas @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cas-verification-happy.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

AI generates a math explanation → SymPy sidecar verifies → verified-green badge surfaces to student.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | `CasVerificationBinding` row with `Status=Verified` |
| NATS | Round-trip on `cena.cas.verify.*` subject |
| DOM | Verified badge visible |

## Regression this catches

Sidecar timeout silently skipping verification (shipgate CI scanner should also catch).

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p0`
