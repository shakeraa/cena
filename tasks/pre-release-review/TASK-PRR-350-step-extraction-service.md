# TASK-PRR-350: `StepExtractionService` — MSP output → canonical step sequence

**Priority**: P0
**Effort**: L (2-3 weeks)
**Lens consensus**: persona #6 engineering, #7 ML safety
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev (ML-adjacent) + math-SME (LaTeX parsing validation)
**Tags**: epic=epic-prr-j, priority=p0, core, depends-on-epic-prr-h
**Status**: **Blocked** — depends on EPIC-PRR-H §3.1 PRR-244..246 MSP intake
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Given MSP structured vision-LLM output (from EPIC-PRR-H §3.1), produce a canonical ordered step sequence usable by the CAS chain. Each step is (LaTeX representation + SymPy AST + confidence + original-bounding-box).

## Scope

- Input: MSP-envelope structured vision-LLM output with extracted expressions.
- Output: `StepSequence { steps: [{latex, ast, confidence, bbox}] }`.
- Handle: numbered steps, arrow notation, equation-manipulation chains.
- Reject: mixed-problem contamination (two different problems on one page).
- Confidence aggregation: per-step confidence consumed by [PRR-351](TASK-PRR-351-ocr-confidence-gate.md).
- LaTeX → SymPy AST translation via existing CAS pipeline.
- Track unparseable steps explicitly rather than silently dropping.

## Files

- `src/backend/Cena.Diagnostic/StepExtraction/StepExtractionService.cs`
- `src/backend/Cena.Diagnostic/StepExtraction/StepSequence.cs`
- Tests: numbered-step layout, arrow-notation chain, unparseable-step flagging.

## Definition of Done

- Given well-formed MSP output, produces valid step sequence.
- Unparseable steps surfaced, not silently dropped.
- Full sln green.

## Non-negotiable references

- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md) — all steps must be SymPy-parseable or explicitly flagged.
- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-351](TASK-PRR-351-ocr-confidence-gate.md), [PRR-360](TASK-PRR-360-step-chain-verifier.md)
- [EPIC-PRR-H §3.1](EPIC-PRR-H-student-input-modalities.md#31-q1-photo-of-solution-msp-architecture)
