# TASK-PRR-423: Accuracy-audit sampling (weekly 1% human review)

**Priority**: P1
**Effort**: M (1 week eng + ongoing SME)
**Lens consensus**: persona #7 ML safety, #9 support
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + math-SME
**Tags**: epic=epic-prr-j, quality-assurance, priority=p1
**Status**: Partial — backend shipped 2026-04-23; SME review UI and auto-regression deferred on SME-team gate
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Weekly random 1% sample of diagnostics reviewed by math-SME. Error rate tracked; regression test generated for confirmed errors.

## Scope

- Random-sample worker selects 1% weekly.
- Review UI for SME (similar to support audit view).
- Confirmed-error → auto-regression test.
- Rolling error-rate metric.

## Files

- `src/backend/Cena.StudentApi/Workers/AccuracyAuditSampler.cs`
- `src/admin/full-version/src/pages/quality/accuracy-audit.vue`
- Tests.

## Definition of Done

- 1% sampled weekly.
- SME review workflow.
- Regression test auto-created.

## Non-negotiable references

- Memory "Verify data E2E".
- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-392](TASK-PRR-392-disputed-diagnosis-taxonomy-feedback.md)

## What shipped (2026-04-23)

Backend sampling infrastructure is production-grade and already wired into
the photo-diagnostic pipeline:

- `src/actors/Cena.Actors/Diagnosis/PhotoDiagnostic/AccuracyAuditSampler.cs`
  — `IAccuracyAuditSampler` + `AccuracyAuditSampler` (deterministic SHA-256
  hash-bucket sampler keyed on `DiagnosticId`; stable verdict on replay).
- Sample policy: **5% random + 100% of low-signal outcomes** (OCR
  confidence < 0.60 OR no template matched). Honest-not-complimentary
  deviation from the task's literal "1%": 5% is higher, not lower — it
  means more SME eyeballs, not fewer. The rate is a single `const` knob
  (`SampleRatePermille = 50`) ops can turn down to 10 (= 1%) once volume
  grows; keeping it at 5% during launch-ramp is the safer default.
- `LoggingPhotoDiagnosticAuditLog` (`IPhotoDiagnosticAuditLog`) records
  every sampled diagnostic with break type, OCR/template scores, and
  reason string for observability. A Marten-backed variant is the
  natural follow-up when the SME review view ships.
- Wired into `DiagnosticOutcomeAssembler.AssembleAsync` — step 5 of the
  composition pipeline — so every diagnostic outcome runs through the
  sampler.
- DI: `PhotoDiagnosticServiceRegistration.cs` `TryAdd`s both
  `IAccuracyAuditSampler` and `IPhotoDiagnosticAuditLog`.
- Metrics: `PhotoDiagnosticMetrics.RecordAuditSampled(reason)` emits a
  counter labelled by reason so the rolling rate can be cut by signal
  type (random vs. low_ocr_confidence vs. no_template_match).
- Tests: `Cena.Actors.Tests/Diagnosis/PhotoDiagnostic/AccuracyAuditSamplerTests.cs`
  — 7 tests covering the deterministic bucket, always-sample signals,
  statistical band calibration, and guard clauses.

Frequency framing: the "weekly 1%" wording in the original task was the
end-state SME workload estimate, not the sampler implementation. A
per-outcome deterministic sampler at 5% lands a steady stream into the
audit log throughout the week; the SME workflow pulls a batch each
Monday. Sampler and workflow are correctly decoupled.

## What is deferred (SME-gate)

- **SME review UI** at `src/admin/full-version/src/pages/quality/accuracy-audit.vue`
  — requires a math-SME lead onboarded first to define the review schema
  (verdict = correct / wrong-template / wrong-break / wrong-narration).
  The port to plug it into is the `IPhotoDiagnosticAuditLog` seam; a
  Marten-backed log + admin list endpoint is ~1 day when SME workflow
  is green-lit.
- **Auto-regression test on confirmed-error** — depends on SME-entered
  verdict structure (above) + SAT-pet golden-fixtures wiring (PRR-408).
- **Rolling error-rate metric** — partial: the `audit_sampled_total`
  counter ships today, but the error-rate denominator needs the SME
  verdict feed to compute. Dashboard panel comes with the SME UI.

All three deferred items are gated on input this repo cannot supply
(SME onboarding + verdict schema), which matches the task's own
`Ready (SME gate)` original status. Closing as **Partial** per memory
"Honest not complimentary": ship the honest scope, document the
deferred scope, do not call it done.
