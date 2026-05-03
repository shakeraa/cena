# TASK-PRR-382: Expandable "show my work" CAS-chain view

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #3 teacher (auditability), #4 curious-student (transparency)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + math-render
**Tags**: epic=epic-prr-j, ux, teacher-facing, priority=p0
**Status**: Partial — CasChainExporter with HE/AR/EN operation labels + 20 tests already shipped via PRR-363; Vue `ShowMyWorkDrawer.vue` + endpoint wiring deferred on frontend gate
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Expandable drawer surfacing the CAS verification chain from [PRR-363](TASK-PRR-363-cas-chain-export-format.md) — per-step: what the student wrote, expected vs. detected expression, operation applied, equivalence result. Teacher-auditable evidence.

## Scope

- Rendered KaTeX math, LTR inside RTL per memory "Math always LTR".
- Operation labels HE/AR/EN.
- Collapsed by default; student/teacher opts in.
- Copy-to-clipboard for teacher to include in feedback.

## Files

- `src/student/full-version/src/components/diagnostic/ShowMyWorkDrawer.vue`
- Tests.

## Definition of Done

- Drawer renders full CAS chain.
- Math displays LTR.
- Copy-to-clipboard works.

## Non-negotiable references

- Memory "Math always LTR".
- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-363](TASK-PRR-363-cas-chain-export-format.md), [PRR-380](TASK-PRR-380-diagnostic-result-screen.md)

## Audit (2026-04-23)

### Already shipped

[CasChainExporter.cs](../../src/actors/Cena.Actors/Diagnosis/PhotoDiagnostic/CasChainExporter.cs)
(188 LOC) — pure function that folds a `StepChainVerificationResult`
from `IStepChainVerifier` into the wire DTO the Vue drawer renders.
Per-step fields + locale-resolved operation labels (expand / factor /
simplify / equate / rearrange) in en / he / ar. Unknown operations
render with the neutral "step" label so a SymPy output the exporter
doesn't recognise still renders cleanly. Tests:
[CasChainExporterTests.cs](../../src/actors/Cena.Actors.Tests/Diagnosis/PhotoDiagnostic/CasChainExporterTests.cs)
— 20 tests covering every operation class, locale branch, unknown-
operation fallback, and empty-chain handling. All pass.

Pipeline upstream:

- `IStepChainVerifier` + `StepChainVerifier` already ship CAS
  verification per step with a `StepChainVerificationResult` trace.
- `DiagnosticOutcomeAssembler.AssembleAsync` runs the verifier on
  every photo-diagnostic outcome.
- Locale normalisation is pinned at the exporter boundary — the
  verifier does not know the locale, the UI does not know the CAS
  plumbing; the exporter is the seam.

### What is deferred (frontend + endpoint gate)

- **`src/student/full-version/src/components/diagnostic/ShowMyWorkDrawer.vue`**
  — Vue drawer that consumes the exported DTO, renders KaTeX math
  inside `<bdi dir="ltr">` within RTL pages (memory "Math always LTR"),
  copy-to-clipboard button. Pure frontend; exporter output is the
  frozen contract.
- **Endpoint `GET /api/me/diagnostic/{diagnosticId}/cas-chain?locale=...`**
  — thin controller that loads the diagnostic's `StepChainVerificationResult`
  and returns `CasChainExporter.Export(...)` under the Premium feature
  fence. Exporter is the hard part; the endpoint is ~50 LOC of
  auth-guarded wiring. Deliberately deferred so the drawer/endpoint
  pair can land as one frontend-plus-BFF PR when the Vue team picks
  up the work.

Closing as **Partial** per memory "Honest not complimentary": the
hardest backend contribution (locale-aware step-by-step CAS DTO
shape) is done and tested; the endpoint + drawer are frontend-
consumer work the Vue team owns.
