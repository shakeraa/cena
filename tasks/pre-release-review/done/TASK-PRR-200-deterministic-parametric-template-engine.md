# TASK-PRR-200: Deterministic parametric template engine (Strategy 1, no-LLM)

**Priority**: P0 — ship-blocker (foundation for epic E coverage waterfall)
**Effort**: L — 5-8 days
**Lens consensus**: persona-cogsci, persona-educator, persona-ministry, persona-finops, persona-redteam
**Source docs**: `docs/research/cena-question-engine-architecture-2026-04-12.md:§4.1` (Strategy 1 — Parametric Templates), `docs/adr/0002-sympy-correctness-oracle.md`, `docs/adr/0032-cas-gated-question-ingestion.md`
**Assignee hint**: kimi-coder (backend)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+educator+ministry
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Ship a deterministic, no-LLM parametric template engine that fills the Strategy 1 codepath the architecture doc describes but the codebase does not yet implement. Templates carry symbolic slots; SymPy constructs the matching stem, canonical answer key, and distractors from the slot values. Output flows into `QuestionBankService` through `ICasVerificationGate` identically to human-authored questions. No LLM is invoked at any point in this codepath — that non-invocation is the architectural contribution.

## Why this matters

The architecture doc quotes "~80% of Bagrut algebra/trig/calculus items are parametrizable" as the theoretical reach of Strategy 1. Today the codebase has only the LLM+CAS path (`AiGenerationService`), which means every variant costs an LLM call and the 80% cost-free path is unrealized. At 10k-student scale, this is the single largest cost lever in the content stack (persona-finops). It is also the only codepath where CAS is the sole source of truth for the answer — no LLM hallucination surface (persona-redteam).

## Files

- `src/actors/Cena.Actors/QuestionBank/Templates/ParametricTemplate.cs` (new) — template record with slots, constraints, answer-derivation strategy
- `src/actors/Cena.Actors/QuestionBank/Templates/ParametricSlot.cs` (new) — slot type enum + constraint DSL
- `src/actors/Cena.Actors/QuestionBank/Templates/ParametricCompiler.cs` (new) — slot-value generator with constraint satisfaction; produces a `QuestionDocument` draft
- `src/actors/Cena.Actors/QuestionBank/Templates/IParametricRenderer.cs` (new) — interface; SymPy-sidecar implementation lives alongside `SymPySidecarClient`
- `src/actors/Cena.Actors/QuestionBank/Templates/SymPyParametricRenderer.cs` (new) — calls the sidecar to realize the template into stem + answer + distractors
- `src/api/Cena.Admin.Api/Templates/TemplateGenerateEndpoint.cs` (new) — POST endpoint for batch generation from a template
- `src/actors/Cena.Actors.Tests/QuestionBank/Templates/` — unit tests (constraint sat, distractor quality, answer canonicalization)
- `contracts/questions/parametric-template.schema.json` (new) — shipped schema for admin authoring (consumed by prr-202)

## Non-negotiable references

- ADR-0002 (SymPy oracle) — the compiler uses the SymPy sidecar for every substitution; no in-process guess.
- ADR-0032 (CAS-gated ingestion) — output funnels through `ICasVerificationGate.VerifyForCreateAsync`; unverified outputs are counted and dropped.
- ADR-0043 (Bagrut reference-only) — templates that were authored from a Bagrut item carry a `SourceAttribution` field and are similarity-checked against the Ministry corpus by prr-201.

## Definition of Done

- `ParametricCompiler.CompileAsync(template, seed, count)` returns exactly `count` variants when the slot constraint space allows it; throws `InsufficientSlotSpaceException` when it does not (no silent partial result).
- Every returned variant has a CAS-verified answer key; variants that fail the gate are dropped and counted in a `ParametricDropReason` structure analogous to `CasDropReasons` in `AiGenerationService`.
- Distractor generation: for MCQ templates, distractors are derived from known misconception classes mapped to the template's concept tag (e.g. sign-flip, arithmetic-shift, coefficient-swap). No distractor is a trivially-wrong number.
- Deterministic: given the same `(template, seed)`, the compiler returns identical output across runs. Seed is persisted on the `QuestionDocument` for reproducibility (forensic requirement for disputed questions).
- No LLM import: `ParametricCompiler.cs` and `SymPyParametricRenderer.cs` have a test that greps the source tree and asserts zero imports of `Anthropic`, `OpenAI`, `ITutorLlmService`, or any other LLM interface. A CI rule (extended by prr-211) enforces.
- Metrics: `cena_parametric_compiled_total{status}`, `cena_parametric_compile_duration_seconds`, `cena_parametric_drop_reason_total{reason}`.
- Tests cover: constraint satisfaction (discriminant ≥ 0 for quadratic-with-real-roots), distractor diversity (no two distractors within ε), deterministic replay, CAS drop accounting, insufficient-slot-space error.
- Full `Cena.Actors.sln` clean build.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: distractor design must map to misconception catalog entries, not be arbitrary numeric perturbations. Owned here. → DoD bullet "Distractor generation".
- **persona-educator**: templates carry `methodology ∈ {Halabi, Rabinovitch}` per ADR-0040; a template authored under one methodology does not count as coverage for the other. Owned here. → Template schema requires `methodology` enum.
- **persona-ministry**: similarity check against Ministry corpus enforced in prr-201, but the template record itself carries `BagrutSource` attribution where applicable. Owned here. → Template schema.
- **persona-finops**: zero LLM calls in this path — enforced by the no-import test and the prr-211 scanner extension. Owned here.
- **persona-redteam**: admin-supplied LaTeX in a template goes through `LatexSanitizer` (§28 of engine doc) before SymPy sees it. Owned here. → DoD bullet implicit in sanitizer wiring.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Consumer: prr-201 (waterfall orchestrator), prr-202 (admin authoring UI)
- Architecture reference: §4.1 of `docs/research/cena-question-engine-architecture-2026-04-12.md`

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect) and [README](./README.md#implementation-protocol-senior-architect). Duplicated-here protocol points:

- **Ask why**: because the 80% Strategy 1 ceiling is unrealized code — today every variant costs an LLM call.
- **Ask how**: interacts with `QuestionBank` aggregate and the `CasRouter` aggregate; respects event sourcing by funneling through `QuestionBankService`; never writes directly to `QuestionState`.
- **Before commit**: full sln build, no-LLM-import test green, CAS drop accounting covered by test.
- **If blocked**: fail loudly; do not reduce scope to "produce an LLM fallback" — the fallback path is prr-201's job, not this task's.
