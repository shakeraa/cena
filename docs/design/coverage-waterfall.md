# Coverage Waterfall Orchestrator (Strategy 1 → 2 → 3) — Design Note

**Task**: prr-201 | **Epic**: PRR-E | **ADRs**: 0002 (CAS oracle), 0026 (LLM tier routing), 0032 (CAS-gated ingestion), 0043 (Bagrut reference-only) | **Status**: Proposed 2026-04-21 | **Builds on**: prr-200 parametric engine

## Why cascade, not parallel

We pay the cost of the **cheapest strategy that can fulfil the rung's target**, and only escalate when it fails. Parallel fan-out would either (a) spend LLM tokens while a parametric template was already sufficient, or (b) queue a curator while stages 1+2 could still have delivered. The waterfall is a cost gradient:

| Stage | Marginal cost per accepted variant | When we stop |
|-------|------------------------------------|---------------|
| 1 — Parametric (prr-200) | **$0** — pure compute + CAS round trip | variants ≥ target, OR template exhausts / missing |
| 2 — LLM isomorph | **~$0.002–$0.015** — tier3 Sonnet call via `AiGenerationService`, plus a CAS round trip per candidate | target met, OR per-institute daily budget hit, OR N attempts exhausted |
| 3 — Curator queue | **human time** — measured in hours, not cents | target met by human author (days later) |

Hitting stage 2 means stage 1's template did not exist or could not stretch to `targetCount`. Hitting stage 3 means the LLM failed too (distractors mismatched, CAS rejected every candidate, or the ministry-similarity checker tripped per ADR-0043). We do not fan stages out in parallel because the expected-value of each subsequent stage is conditional on the prior failing.

## Shape of the orchestrator

```
ICoverageWaterfallOrchestrator.FillRungAsync(cell, targetCount)
  → Stage 1: ParametricCompiler.CompileAsync → N1 accepted variants
      if N1 >= targetCount: return { strategies: [S1], filled: N1, curator: null }
  → Stage 2: IIsomorphGenerator.GenerateAsync(seedVariants=N1, need=targetCount-N1)
      each candidate:
        - MinistrySimilarityChecker.Score > 0.82 → drop (ADR-0043)
        - ICasVerificationGate.VerifyForCreateAsync → drop if Failed/Unverifiable
        - deduper.TryAdmit → drop if canonical hash collides (cross-strategy)
      if N1+N2 >= targetCount: return { strategies: [S1,S2], filled: N1+N2, curator: null }
  → Stage 3: ICuratorQueueEmitter.EnqueueAsync(cell, gapCount, priorStageDrops)
      return { strategies: [S1,S2,S3], filled: N1+N2, curator: TaskId }
```

A cell is the coverage-matrix coordinate `(track, subject, topic, difficulty, methodology, questionType)`. Methodology matters (ADR-0040): a Halabi cell cannot be filled by a Rabinovitch variant. The cell also carries a `Language` so the curator task is actionable.

## Non-negotiable constraints

1. **CAS gate runs on every stage's output.** ADR-0002 — no math reaches students unverified. The orchestrator does not have a fast path that skips it.
2. **LLM path goes through `[TaskRouting]`.** ADR-0026 §3 — tier3, `question_generation`. We reuse `IAiGenerationService.BatchGenerateAsync` which already carries the attribute.
3. **Ministry similarity checker on stage 2 only.** ADR-0043 — stage 1 variants are authored against a re-written template and cannot inherit Ministry text; stage 2 candidates can echo Ministry phrasing verbatim and must be filtered.
4. **Deduper is cross-strategy.** Reuse `ParametricVariantDeduper.ComputeCanonicalHash` so a stage-2 isomorph cannot ship the same canonical form as a stage-1 variant.
5. **Budget cap is per-institute-per-day.** Default $50/day; configurable via `CoverageWaterfallOptions`. Exceeding it short-circuits to stage 3 with drop-reason `BudgetCap`.
6. **Idempotency on stage 3.** Curator task creation uses a deterministic id `sha256(cellAddress + "/" + releaseDate)`; re-running the orchestrator on the same red cell does not enqueue a second task.

## Why a waterfall result (not an exception-on-failure)

`WaterfallResult` always returns successfully with a per-stage breakdown. An incomplete fill is **not** an error — it is the normal path for a newly-authored topic where no parametric template exists yet. The caller (prr-210 ship-gate) reads the `Filled < Target` bit and decides whether to block release; telemetry `cena_coverage_cell_gap_total` rises on every gap.

## What this does NOT do

- Does **not** own the coverage matrix projection — that's prr-209.
- Does **not** author templates or prompts — stage 1 consumes a template picked by id, stage 2 hands the existing parametric seed variants to the LLM as few-shot examples.
- Does **not** publish variants to the question bank — it returns the accepted variants to the caller; publishing is the caller's write path so ingestion events (`QuestionAiGenerated_V2`, `QuestionIngested_V2`) carry the correct provenance.
- Does **not** do per-tenant cost accounting — the budget gate is per-institute but the cost metric is global; prr-046 owns the institute-level cost dashboard.

## Test matrix

| # | Test | Assertion |
|---|------|-----------|
| 1 | `Strategy1SufficientNoLlmCalls` | template yields ≥target → zero calls to `IIsomorphGenerator` |
| 2 | `Strategy1PartialTriggersStrategy2` | template yields half; isomorph generator fills rest; all CAS-verified |
| 3 | `CasFailureOnStrategy2Drops` | isomorph returns a candidate CAS rejects → dropped with reason `CasRejected`, not shipped |
| 4 | `MinistrySimilarityTooHigh` | isomorph returns Ministry-verbatim text → dropped with reason `MinistrySimilarity` (ADR-0043) |
| 5 | `BudgetCapSkipsStrategy2` | institute is over $50/day → stage 2 skipped entirely, drop reason `BudgetCap`, curator enqueued |
| 6 | `DedupeAcrossStrategies` | isomorph returns a canonical form already emitted by stage 1 → dropped with reason `Duplicate` |
| 7 | `AllThreeStagesCascade` | no template + no LLM variants → stage 3 curator task enqueued with gap count and drop reasons |
| 8 | `CuratorEnqueueIsIdempotent` | running twice for the same unfilled cell does not enqueue twice |

## File inventory (all ≤500 LOC)

```
src/actors/Cena.Actors/QuestionBank/Coverage/
  CoverageCell.cs                     // cell record + canonical address string
  WaterfallStage.cs                   // enum + per-stage result record
  WaterfallResult.cs                  // aggregate result
  WaterfallDropReason.cs              // drop-reason taxonomy
  IIsomorphGenerator.cs               // Cena.Actors-side contract (Admin.Api implements)
  ICuratorQueueEmitter.cs             // curator queue contract + in-memory test impl
  CuratorQueueItem.cs                 // enqueued record
  MinistrySimilarityChecker.cs        // deterministic n-gram cosine (no LLM)
  CoverageWaterfallOptions.cs         // budget cap, thresholds
  CoverageWaterfallOrchestrator.cs    // the cascade
  CoverageWaterfallMetrics.cs         // Meter 'Cena.Coverage'

src/actors/Cena.Actors.Tests/QuestionBank/Coverage/
  CoverageWaterfallOrchestratorTests.cs
  MinistrySimilarityCheckerTests.cs
  FakeIsomorphGenerator.cs            // test double driving stage 2
  InMemoryCuratorQueue.cs             // test double for stage 3

src/api/Cena.Admin.Api/QuestionBank/Coverage/
  AiIsomorphGenerator.cs              // implements IIsomorphGenerator by
                                      // delegating to AiGenerationService.BatchGenerateAsync
```

Non-files on this task: no DB schema, no Marten projection, no admin endpoint — those belong to prr-209.
