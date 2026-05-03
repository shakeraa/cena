# TASK-E2E-G-02: Parametric template authoring (prr-202)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @cas @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/parametric-template.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Admin authors deterministic parametric template → live preview renders → CAS gate verifies → admin submits → batch generation → new questions persisted via `CasGatedQuestionPersister`.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Authoring UI + preview + CAS-verified badge |
| DB | `ParametricTemplate` event stream |
| CAS | SymPy sidecar round-trip |
| Architecture-test | No-LLM in parametric pipeline (Strategy 1 purity) |

## Regression this catches

LLM slipping into parametric pipeline; unverified templates reach students; batch generation writes to wrong tenant.

## Done when

- [ ] Spec lands
