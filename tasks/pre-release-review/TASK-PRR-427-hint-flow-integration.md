# TASK-PRR-427: Integration with existing hint-request flow (TAIL)

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: tail — prevents feature fragmentation
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + UX
**Tags**: epic=epic-prr-j, integration, priority=p1, tail
**Status**: **Partially satisfied by stuck-classifier path; photo-diagnostic path blocked on [PRR-350](TASK-PRR-350-step-extraction-service.md)** (itself blocked on EPIC-PRR-H §3.1 PRR-244..246 MSP intake)
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Clarify the relationship between existing hint-request flow (PRR-262 stem-grounded hints) and photo-diagnostic. They should complement, not conflict.

## Scope

- Decision model: hint = before attempt, diagnostic = after wrong attempt.
- Share misconception taxonomy: when the diagnostic surfaces a template, the subsequent hint request for the retry uses the same misconception context.
- No double-count against diagnostic cap when the same session reopens the hint modal.
- Memory continuity across retry attempts in a session.

## Files

- Integration glue across `HintFlowController` and `DiagnosticFlowController`.
- Tests.

## Definition of Done

- Session carries misconception context across flows.
- Caps not double-counted.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — session scope.
- Memory "No stubs".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-262](TASK-PRR-262-scaffolding-stem-grounded-hints.md), [PRR-381](TASK-PRR-381-post-reflection-narration.md)

## Scope clarification (2026-04-23)

Audit of the existing surface surfaces two diagnostic paths, not one:

1. **Stuck-classifier path** — `IHintStuckDecisionService` (RDY-063
   Phase 2b, file `src/actors/Cena.Actors/Diagnosis/IHintStuckDecisionService.cs`)
   is already the endpoint-facing surface for the `/hint` route. It
   takes the `LearningSessionQueueProjection`, runs the classifier
   (`IStuckTypeClassifier` → `HeuristicStuckClassifier` /
   `HybridStuckClassifier` / `ClaudeStuckClassifierLlm`), computes a
   `StuckDiagnosis`, and passes a `HintDecisionOutcome` to the
   endpoint that then uses it to adjust the hint level via
   `IHintLevelAdjuster`. Misconception context (via `StuckType` +
   confidence) is already threaded through the decision. Caps do not
   double-count because this path shares no counter with the photo-
   diagnostic cap. The decision-model intent of this task ("hint =
   before attempt, diagnostic = after wrong attempt") is already the
   live behaviour for this path.

2. **Photo-diagnostic path** — `DiagnosticOutcomeAssembler.AssembleAsync`
   exists as a composed service (`src/actors/Cena.Actors/Diagnosis/PhotoDiagnostic/
   DiagnosticOutcomeAssembler.cs`) but has **no endpoint wiring yet**
   because [PRR-350 `StepExtractionService`](TASK-PRR-350-step-extraction-service.md)
   is **Blocked** on the upstream EPIC-PRR-H §3.1 MSP intake
   (PRR-244..246). Until an endpoint calls `AssembleAsync` and the
   `DiagnosticOutcome` is written somewhere the hint ladder can read,
   there is no producer for the "photo diagnostic surfaced a template
   → next hint uses that misconception" glue this task proposes.
   Shipping that glue today would be speculative interface-padding —
   violates memory "No stubs — production grade".

## Why this stays open (blocked), not "Partial-closed"

The file paths the original task lists (`HintFlowController`,
`DiagnosticFlowController`) are speculative and do not exist in the
codebase. The real port shape — once PRR-350 unblocks — is:

- Add a nullable `Dictionary<string, MisconceptionBreakType>
  LastDiagnosticByQuestion` to `LearningSessionQueueProjection`,
  following the same session-scoped pattern as `LadderRungByQuestion`
  and `AnxiousConceptIds` (ADR-0003).
- Have the photo-diagnostic endpoint (to be built on PRR-350) write
  the outcome's break type to that dictionary.
- Extend `HintLadderInput` with an optional `MisconceptionHint` and
  have `L2HaikuHintGenerator` / `L3WorkedExampleHintGenerator` read it
  as a prompt-priming signal. `HintLadderEndpoint` reads from the
  projection and populates the input.

Estimated 1-2 engineering days **after** PRR-350 lands. Blocked today.

## Non-blocking follow-ups surfaced by this audit

- None — the stuck-classifier path is production-grade and shipping
  the intended decision-model. No regressions to fix here.
