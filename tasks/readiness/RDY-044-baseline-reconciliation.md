# RDY-044: CAS Conformance Baseline — Reconcile Numbers

- **Priority**: High — doc integrity
- **Complexity**: Low
- **Effort**: 1-2 hours
- **Dependencies**: RDY-043 nightly run green

## Problem

`ops/reports/cas-conformance-baseline.md` enumerates **27 cases**. `CasConformanceSuiteRunner` filters 450 placeholders and the suite file has ~50 concrete pairs. No nightly has run to populate the measured number. Flipping Enforce in prod is blocked on this.

## Scope

- First nightly run publishes measured numbers (pair count + engine-agreement + router pass rate) into `ops/reports/cas-conformance-baseline.md`
- Unify the narrative around one measured number
- Gate the "Enforce in prod" flag on a green nightly present in the report

## Acceptance

- [ ] Baseline doc shows a measured (not target) pass rate
- [ ] ADR-0032 addendum names the measured rate and the CI gate
