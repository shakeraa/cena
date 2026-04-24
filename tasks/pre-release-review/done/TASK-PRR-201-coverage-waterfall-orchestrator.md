# TASK-PRR-201: Coverage waterfall orchestrator (template → LLM-isomorph → curator queue)

**Priority**: P0 — ship-blocker (foundation for 100% coverage claim)
**Effort**: L — 6-10 days
**Lens consensus**: persona-cogsci, persona-educator, persona-ministry, persona-finops, persona-sre, persona-redteam
**Source docs**: `docs/research/cena-question-engine-architecture-2026-04-12.md:§4` (Strategies 1/2/3), `docs/adr/0043-bagrut-reference-only-enforcement.md`, `docs/adr/0032-cas-gated-question-ingestion.md`
**Assignee hint**: claude-subagent-waterfall (backend)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+educator+ministry+finops
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Glue the three variant-generation strategies into a single waterfall that guarantees every `(track, topic, difficulty, methodology, question_type)` cell in the coverage matrix reaches the prr-210 SLO. Stages, in order: (1) deterministic parametric (prr-200), (2) LLM isomorph with CAS verification (existing `AiGenerationService`), (3) human-curator queue. Every cell that is still red after stage 2 creates a curator task with a deadline. No silent failures, no unexplained gaps.

## Why this matters

The design doc's Strategy 1/2/3 are three independent codepaths today with no orchestration layer above them. The question "is topic X at difficulty Y fully covered?" has no single answer source. This task turns the waterfall into a first-class service, produces the telemetry prr-209 needs for the admin heatmap, and is the mechanism by which prr-210 can gate the release.

## Files

- `src/actors/Cena.Actors/QuestionBank/Coverage/CoverageOrchestrator.cs` (new)
- `src/actors/Cena.Actors/QuestionBank/Coverage/CoverageMatrix.cs` (new) — cell definition + ready-count projection
- `src/actors/Cena.Actors/QuestionBank/Coverage/CoverageProjection.cs` (new) — Marten projection from `QuestionAuthored_V2 | QuestionAiGenerated_V2 | QuestionIngested_V2` to `CoverageCellState`
- `src/actors/Cena.Actors/QuestionBank/Coverage/WaterfallStage.cs` (new) — stage enum + per-stage result record
- `src/actors/Cena.Actors/QuestionBank/Coverage/MinistrySimilarityChecker.cs` (new) — rejects LLM-isomorph outputs too close to the Ministry corpus per ADR-0043
- `src/actors/Cena.Actors/QuestionBank/Coverage/CuratorTaskEmitter.cs` (new) — emits a task with a deadline when stage 2 fails to fill a cell
- `src/api/Cena.Admin.Api/Coverage/CoverageStatusEndpoint.cs` (new) — read model for prr-209
- `src/actors/Cena.Actors.Tests/QuestionBank/Coverage/` — unit + projection tests

## Non-negotiable references

- ADR-0043 (Bagrut reference-only) — stage 2 output is rejected when `MinistrySimilarityChecker.Score > 0.82` (threshold tuned in calibration task).
- ADR-0032 (CAS-gated ingestion) — every stage writes through the gate.
- ADR-0002 (SymPy oracle) — no variant reaches a cell without CAS Verified status.
- ADR-0001 (tenant isolation) — coverage is computed globally for the content corpus (not per tenant), but the read model respects tenant scope when surfaced through prr-209's teacher-facing view.

## Definition of Done

- `CoverageOrchestrator.FillCellAsync(cell, targetCount)` runs stages 1 → 2 → 3 and returns a `WaterfallResult` listing per-stage counts, drops, and whether a curator task was created.
- Stage 1 invokes `ParametricCompiler`; if no matching template, falls through (not failure).
- Stage 2 invokes `AiGenerationService.BatchGenerateAsync` with the cell's parameters; outputs run through `MinistrySimilarityChecker` before CAS gate.
- Stage 3 emits a `CuratorTask` with a deadline calculated from the target release date; the task carries the cell address, the prior-stage drops with reasons, and a link to the nearest-similar Bagrut reference.
- `CoverageProjection` updates on every `QuestionAuthored_V2 | QuestionAiGenerated_V2 | QuestionIngested_V2` event; read model returns in ≤100ms p99.
- `MinistrySimilarityChecker` uses a deterministic embedding + cosine metric; score surfaced in the drop reason; threshold is a ConfigMap value.
- Telemetry: `cena_coverage_cell_filled_total{stage,result}`, `cena_coverage_cell_gap_total{track,subject,difficulty,methodology}`, `cena_coverage_fill_duration_seconds`, `cena_ministry_similarity_rejected_total`.
- Integration test: seed the event store with a known corpus, run the orchestrator across a small matrix, assert the projection lands at the expected cell state.
- Stage-3 curator task creation is idempotent on re-run; it does not duplicate tasks for the same cell.
- Full `Cena.Actors.sln` clean build.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-waterfall --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: stage 2 outputs inherit distractor requirements from prr-200 — the orchestrator rejects LLM isomorphs whose distractors are not misconception-mapped. Owned here.
- **persona-educator**: methodology axis must be preserved through stages — a Halabi cell cannot be "filled" by a Rabinovitch variant. Owned here. → DoD bullet implied by cell identity.
- **persona-ministry**: `MinistrySimilarityChecker` is owned here; its threshold is reviewable and logged per rejection.
- **persona-finops**: per-institute and global cost caps on stage 2 enforced via `IsAllowedForInstituteAsync` check; stage 2 aborts with curator-task-creation when cap is hit. Owned here.
- **persona-sre**: orchestrator is async, idempotent, and resumable — a partial run does not leave the coverage projection in a half-updated state. Owned here.
- **persona-redteam**: stage 2 output sanitized through the same LaTeX pipeline as ingestion before CAS gate; no admin bypass.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-200 (stage 1), existing `AiGenerationService` (stage 2)
- Consumer: prr-209 (admin heatmap), prr-210 (CI ship-gate)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect). Key points:

- **Ask why**: because today there is no single source of truth for "is this cell covered?" — the question is unanswerable at the CLI, in CI, or in the admin UI.
- **Ask how**: the orchestrator is a domain service on the `QuestionBank` aggregate boundary; it writes only via existing aggregates; it reads via projections.
- **Before commit**: integration test passes with real Marten projection rebuild; no cell can be marked "filled" without CAS Verified and QG ≥ 85.
- **If blocked**: fail loudly; do not silently weaken the similarity threshold to make a test pass.
