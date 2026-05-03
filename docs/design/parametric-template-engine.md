# Parametric Template Engine (Strategy 1) — Design Note

**Task**: prr-200 | **Epic**: PRR-E | **ADRs**: 0002 (CAS oracle), 0032 (CAS-gated ingestion), 0043 (Bagrut reference-only), 0045 (hint tiering) | **Status**: Proposed 2026-04-21

## Why deterministic, not ML-sampled

- **Coverage guarantee**: a `(template, seed-range)` pair enumerates the full Cartesian product of its slot space. We can *prove* that every (topic, difficulty, methodology) rung has ≥N variants by counting, not sampling. An ML sampler cannot.
- **Pedagogical reproducibility**: if a student disputes a variant, we re-run with the stored seed and get byte-identical text, options, and answer. LLM temperature > 0 precludes this; temperature == 0 is still vendor-dependent.
- **Cost**: $0/variant. 10k-student scale × 80% parametrizable means this is the single largest cost lever in content (persona-finops).
- **Safety surface**: CAS is the *only* source of truth for the answer. No LLM hallucination surface exists in this path; the no-import architecture test enforces it (persona-redteam).

## Module layout (all ≤500 LOC)

```
src/actors/Cena.Actors/QuestionBank/Templates/
  ParametricTemplate.cs          // immutable template record + accept-shape enum
  ParametricSlot.cs              // slot DSL: integer/rational ranges, exclude sets, predicates
  ParametricDropReason.cs        // drop-reason taxonomy (parallels CasDropReason)
  InsufficientSlotSpaceException.cs
  ParametricCompiler.cs          // deterministic (template, seed, count) -> IReadOnlyList<Variant>
  ParametricVariantDeduper.cs    // canonical-form hash for near-duplicate rejection
  IParametricRenderer.cs         // stem/answer/distractor rendering contract
  SymPyParametricRenderer.cs     // CAS-backed renderer; every solution through ICasRouterService
  ParametricDistractorGenerator.cs // maps misconception classes -> distractor formulas
  ParametricTemplateMetrics.cs   // Meter 'Cena.Parametric' counters/histograms
  ParametricItemGeneratedEvents.cs // ParametricItemGenerated_V1 (template id + seed + slot snapshot)
```

## Determinism contract

- `CompileAsync(template, seed, count)` is a pure function of `(template.Id, template.Version, seed, count)`.
- Inside the compiler we use `new Random(Unchecked((int)Hash(templateId, templateVersion, seed)))` to derive slot-value draws — never `Random.Shared`, never `Guid.NewGuid`, never `DateTimeOffset.UtcNow`.
- The produced `QuestionDocument.QuestionId` is also derived deterministically (`Sha256(templateId|version|seed|slotJson)`) so the same input regenerates the same id.
- We persist only `(templateId, version, seed, slotSnapshot)` in the `ParametricItemGenerated_V1` event. Rendered stem/options/answer can be regenerated at any time by replaying compile + render.

## CAS gate (ADR-0002) — every variant

Every variant leaves the compiler through `ICasVerificationGate.VerifyForCreateAsync` before it enters `QuestionBankService`. Variants whose CAS verdict is `Failed` (e.g. zero-divisor after slot substitution, non-terminating decimal when `accept_shapes=integer`) are counted in `ParametricDropReason` and dropped — never silently included. `Unverifiable` / `CircuitOpen` variants are also dropped in this path because the whole point of Strategy 1 is "CAS-authoritative" — we do not ship variants we cannot prove correct.

## Dedupe

Two variants are "near-duplicate" if their canonical form — the tuple `(normalized stem skeleton, canonicalised answer via CAS, sorted option canonical forms)` — hashes to the same SHA-256. We reject within a single compile call and across a single template batch; cross-template dedupe is out of scope for prr-200 (prr-201 owns waterfall-level dedupe).

## Coverage accounting

The compiler exposes `CoverageReport CompileWithCoverageAsync(template, seedRange)` returning per-rung variant counts. The CLI harness (`src/tools/Cena.Tools.QuestionGen`) aggregates this across a directory of templates and writes a per-rung count file consumed by prr-210's ship-gate.

## What this does NOT do

- No LLM call anywhere. Architecture test `NoLlmInParametricPipelineTest` scans `src/actors/Cena.Actors/QuestionBank/Templates/**` + `src/tools/Cena.Tools.QuestionGen/**` and asserts zero imports of `Anthropic`, `OpenAI`, `Google.*`, `Azure.AI`, or any `ITutorLlmService` / `IAiGenerationService`.
- No LLM fallback. That is prr-201's waterfall job.
- No cross-template dedupe. That is prr-201 + prr-209.
- No ML difficulty laddering. Per-template `Difficulty ∈ {Easy, Medium, Hard}` is author-declared.

## Test matrix

| # | Test | Assertion |
|---|------|-----------|
| 1 | `DeterminismTest` | `compile(t, 42, 5)` == `compile(t, 42, 5)` over 3 runs |
| 2 | `CasRejectsBadSlotComboTest` | slot combo producing `5/0` is rejected |
| 3 | `AcceptShapesIntegerOnlyTest` | slot combo producing `7/3` is rejected when `accept_shapes=[integer]` |
| 4 | `DedupeTest` | compiling a template whose slot space collapses to 3 canonical forms returns ≤3 variants |
| 5 | `CoverageTenKSeedsTest` | 10k seeds over a quadratic template produces ≥N distinct variants |
| 6 | `InsufficientSlotSpaceTest` | requesting 100 variants from a 4-element slot space throws `InsufficientSlotSpaceException` |
| 7 | `NoLlmInParametricPipelineTest` | source grep finds zero LLM imports in the engine directory |
| 8 | `CasDropReasonAccountingTest` | dropped variants surface with a `ParametricDropReason` entry |
