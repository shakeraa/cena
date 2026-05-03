# Concept Extraction for Mastery Tracking — Research

- **Status**: Research / proposal — not yet ADR-locked
- **Date**: 2026-05-01
- **Author**: claude-code (researcher pass)
- **Scope**: How Cena extracts a per-question concept profile from Bagrut drafts so the BKT mastery engine moves correctly when a student attempts the question.
- **Related ADRs**: [ADR-0002](../adr/0002-sympy-correctness-oracle.md) (CAS oracle), [ADR-0032](../adr/0032-cas-gated-question-ingestion.md) (CAS-gated ingestion), [ADR-0039](../adr/0039-bkt-parameters-and-fading.md) (BKT params locked), [ADR-0050](../adr/0050-multi-target-student-exam-plan.md) (multi-target plans), [ADR-0059](../adr/0059-bagrut-reference-browse-and-variant-generation.md) (Bagrut reference + variant), [ADR-0003](../adr/0003-misconception-session-scope.md) (misconception session scope)
- **Trigger**: User research request 2026-05-01 — current Bagrut draft tagging is ONE keyword-classified taxonomy node per question; mastery tracking needs a richer per-question concept vector.

---

## Iteration 1 — What's already there

### 1.1 The mastery model (it exists, it's BKT-locked)

- **Posterior store**: `SkillKeyedMasteryDocument` (`src/actors/Cena.Actors/Mastery/SkillKeyedMasteryDocument.cs`). One row per `(StudentAnonId, ExamTargetCode, SkillCode)` triple. Each row carries a single BKT `MasteryProbability` ∈ [0.001, 0.999], an `AttemptCount`, an `UpdatedAt`, and a `Source` provenance string. PRR-222 forced exam-target into the key so the same skill at 4u vs 5u depth keeps separate posteriors.
- **Skill identifier**: `SkillCode` (`src/actors/Cena.Actors/Mastery/SkillCode.cs`) — lowercase, dot-hierarchy, `[a-z0-9.-]` only. Examples: `math.algebra.quadratic-equations`, `math.calculus.derivative-rules`. The taxonomy in `scripts/bagrut-taxonomy.json` already maps every leaf to a stable concept id (`ALG-001`, `CAL-002`, ...) and a `bloom_range`. The two id schemes coexist — `SkillCode` is the catalog-level slug, `ALG-001` the seed-data id; they reference the same pedagogical object.
- **The BKT update path**: `ConceptAttempted_V1/V2/V3` in `src/actors/Cena.Actors/Events/LearnerEvents.cs`. **The event carries exactly ONE `ConceptId` field per attempt.** `BktTracer` runs on every such event and folds it into the matching mastery row. `ConceptMastered_V1/V2` fires on threshold cross (0.85 default per `MasteryConstants`).
- **Locked policy**: BKT parameters are locked at Koedinger defaults (PInit=0.30, PLearn=0.15, PSlip=0.10, PGuess=0.15) by ADR-0039 and an arch test. Per-student parameter learning is forbidden by ADR-0003 + ADR-0039.
- **Auxiliary signal**: `MasterySignalEmitted_V1` (one-shot small nudges, default delta 0.05) for post-reflection retry-success and similar pedagogical wins. Also single-skill, single-delta — explicitly NOT a multi-concept channel.

### 1.2 Where concepts are attached to questions today

Three different ingestion paths, three different shapes. This is the actual gap the research is about.

**Path A — Bagrut PDF drafts (the file the user pointed at)**:
`src/api/Cena.Admin.Api/Ingestion/BagrutDraftPersistence.cs:ClassifyTaxonomy(prompt, latex)` → `(string? TaxonomyNode, double Confidence)`. The classifier runs Hebrew/Arabic/English keyword regexes against the OCR'd prompt + LaTeX and returns ONE leaf path like `calculus.applications_of_derivatives` with confidence 0.40–0.65, or `(null, 0.0)` when no keyword fires. The leaf is persisted on `PipelineCuratorMetadata.TaxonomyNode` (the kanban metadata, not the question itself). Curator confirms or overrides during review. The header comment in `ClassifyTaxonomy` already calls itself a "heuristic seed" and notes "a future LLM-based classifier can replace this without changing the call site" — the gap is openly acknowledged.

**Path B — Textbook / explanation OCR (richer)**:
`src/actors/Cena.Actors/Ingest/ContentExtractorService.cs:LinkConcepts(text, subject)` produces a `IReadOnlyList<string>` of concept ids by exact-name string-match against `IConceptGraphCache`. Returns a multi-concept list, defaults to `["unlinked"]` when nothing matches. Result lands on `ContentBlockDocument.ConceptIds` and aggregates into `PipelineItemDocument.LinkedConceptIds`. **This path already supports many-concepts-per-block, but only on textbook content blocks — not on Bagrut question drafts.**

**Path C — Authored / AI-generated question on `QuestionDocument`**:
The persisted question carries:
- `ConceptId : string` — single, required, non-empty (the BKT engine's anchor).
- `LearningObjectiveId : string?` — optional reference to `LearningObjectiveDocument`.
- `Prerequisites : List<string>?` — list of prerequisite concept ids for PSI scaffolding.
- `BagrutAlignment.TopicCluster : string` — coarse cluster like `function_investigation`, `integral_application`, used for CAT stratification.

**`LearningObjectiveDocument` already supports multi-concept** (`ConceptIds: List<string>`) — but each question still references only one LO, and the BKT path still keys on a single `ConceptId`. So even when the LO covers 3 concepts, the runtime BKT update fires on exactly one of them.

### 1.3 What the SymPy CAS oracle contributes — the structural-tag question

The user asked specifically whether the CAS produces structural tags we could mine. **Answer: today, no.** `CasVerifyResult` (`src/actors/Cena.Actors/Cas/CasContracts.cs`) returns `Verified: bool`, `Operation` (one of `Equivalence`/`StepValidity`/`NumericalTolerance`/`NormalForm`/`Solve`/`Canonicalize`), `SimplifiedA`, `SimplifiedB`, an engine name, and latency. The `Operation` is *what kind of check we asked for*, not *what mathematical concepts the question exercises* — running a `Canonicalize` against `(x-2)(x+3)` doesn't tell you the question is about *factoring quadratics* vs. *polynomial expansion* vs. *zeros of a polynomial*. SymPy internally has step-method tags (e.g. `integration_by_parts`, `chain_rule`, `quotient_rule`) but the sidecar contract doesn't surface them. Adding a "method trace" return field to the sidecar is feasible (~50 lines Python) and would give us a *low-noise structural channel* for the procedural concepts a question actually exercises during solve. This is one of the design options in §2.2.

### 1.4 Curator confirm / variant generation flow

Today's flow when a Bagrut draft becomes a published, BKT-eligible question:

1. PDF lands → `BagrutPdfIngestionService` creates a `PipelineItemDocument` + `BagrutDraftPayloadDocument`.
2. `BagrutDraftPersistence` runs `ClassifyTaxonomy` → fills `PipelineCuratorMetadata.TaxonomyNode` (single leaf) + confidence.
3. Curator reviews, confirms / overrides metadata, marks ready.
4. Variant generator (`GenerateVariantsJobStrategy`) reads the draft + metadata, calls the LLM to produce a recreated question, the CAS gate verifies the answer, `CasGatedQuestionPersister.PersistAsync` writes `QuestionState` events + `QuestionDocument` with `ConceptId = <single>` and (optionally) `LearningObjectiveId`.
5. Student attempts → `ConceptAttempted_V3 { ConceptId = <single> }` → BKT fold on one row.

So the choice of "which concept this question is about" is locked by the curator at step 3, *before* the question is even authored. Multi-concept signal is lost at the very first stage.

---

## Iteration 2 — Where the gap is, what decisions are unmade

### 2.1 The actual gap, in one sentence

A real Bagrut Part-B problem ("investigate f(x) = x³ − 6x² + 9x: find extrema, inflection points, and sketch the graph") tests at least four concepts: `derivative-of-polynomial`, `extremum-from-zero-derivative`, `second-derivative-test`, `function-sketching-from-derivative-info`. The current pipeline assigns it ONE id (probably `calculus.applications_of_derivatives`, confidence 0.60) and the BKT engine only ever moves that ONE row when a student attempts it. The student's mastery vector for the three other concepts stays frozen at the prior, even after they correctly demonstrate them on this very question.

This is the worst class of accuracy bug in a mastery system: it's silent, it's structural, and it makes the platform's central pedagogical claim ("we know what you know") not true.

### 2.2 Open design decisions (with options, not answers — this is iteration 2)

| # | Decision | Options | Notes / constraints |
|---|----------|---------|---------------------|
| **D1** | Extraction mechanism | (a) keep keyword/regex (status quo, just scale up to multi-tag); (b) LLM extraction (Claude Haiku per ADR-0026 tier 2); (c) hybrid: rules first, LLM only when rules return <2 hits or low confidence; (d) SymPy method-trace harvest (new sidecar return field) | (b) costs real money; (d) only fires for items the student actually solves through CAS (ingestion-time we have the canonical answer but not a CAS step trace yet — would need to run a "solve and emit method trace" pass). |
| **D2** | Concept granularity | (a) taxonomy leaves only (~80–120 leaves across the 6 domains); (b) finer "skill atoms" below the taxonomy (`derivative-of-polynomial` vs `derivative-of-product` under `calculus.derivative_rules`); (c) Bloom 2-axis (concept × cognitive process) | (a) is what `SkillCode` and the taxonomy already model. (b) needs a new catalog. (c) is what `LearningObjectiveDocument` already models — but it's many-to-one, and BKT keys on `ConceptId` not LO. **Recommendation foreshadowed**: (a) for v1, defer (b). |
| **D3** | Multi-concept weighting | (a) unweighted set — every extracted concept gets the same BKT update; (b) weighted vector — primary concept gets full update, secondary gets discounted update; (c) explicit "primary + supporting" with two semantics: BKT fires only on primary, supporting concepts get a `MasterySignalEmitted_V1` nudge. | BKT math is set up for binary outcome on one skill. Splitting one observation across N skills with weights changes the posterior semantics — it's no longer "the probability the student knows X given their answer to a question that tests X". (c) keeps the BKT semantics clean and reuses an event we already have. |
| **D4** | When does extraction run | (a) at curator-confirm time (offline, before publish); (b) at variant-generate time (the LLM is already reading the prompt to recreate); (c) on-demand at first attempt (lazy); (d) batch / re-extract on taxonomy version bumps | (a) is cleanest — extraction result is part of the published question's identity. (b) folds the LLM cost into a call we're already making — cheapest. (c) blocks the student's request path on an LLM round-trip — bad. **Recommendation foreshadowed**: (b) primary, (a) for non-AI-authored items. |
| **D5** | Storage shape | (a) extend `QuestionDocument.ConceptIds : List<string>` (and keep `ConceptId` as primary for back-compat); (b) separate `QuestionConceptTagDocument` projection; (c) event-sourced `QuestionConceptsExtracted_V1` that the projection folds | The Cena pattern is event-sourced + projection (per `MasteryConstants` design + ADR-0032 §16). (c) is consistent. (a) adds a denormalized read view on the same document for hot-path session queries. **Recommendation foreshadowed**: do BOTH (a)+(c) — emit the event, project onto `QuestionDocument.ConceptIds`. |
| **D6** | Curator validation gate | (a) auto-publish extracted concepts; (b) curator must confirm before publish (block publish if concepts unconfirmed); (c) curator can override but extraction stands by default; (d) sample-based audit — every Nth extraction surfaces for review | Quality drift from silent LLM hallucination is the single biggest risk (see §2.4 below). The user's memory `feedback_no_stubs_production_grade` rules out (a) for the engine. (c) is the practical answer for at-scale operation; (b) is the hard mode for the first 200 ingested items as a calibration corpus. |
| **D7** | Concept-id stability | (a) extraction emits taxonomy-leaf strings (already stable); (b) extraction emits free-form LLM strings → canonicalizer maps to taxonomy; (c) extraction emits both (closed-set primary + free-form rationale string) | Stable ids are non-negotiable for BKT — if the id moves, the row moves and the posterior vanishes. (b) needs a hard canonicalizer with a closed allowlist. (c) is the "show your work" version that gives curators something readable to reject. |

### 2.3 Hard constraints from existing ADRs

- **ADR-0002**: SymPy must verify correctness; the LLM is not the oracle. Concept *extraction* is metadata, not correctness — but extracted concepts that gate mastery updates must still be CAS-coherent. Concretely: if extraction says "this question tests `integration_by_parts`", we need a way to falsify that claim before it pollutes mastery posteriors. The cheapest falsification: the question's CAS-verified canonical solution has to be reachable from the claimed concept set. (Stronger version: SymPy actually solves the question and emits the methods it used; concept claims that no method trace supports get downweighted.)
- **ADR-0003**: Misconception data is session-scoped. **Concept tags on questions are NOT misconceptions.** Concepts describe what a question tests; misconceptions are what a particular student got wrong this session. The two channels stay separate — extraction must not be repurposed as a misconception-mining surface.
- **ADR-0032**: Every math question reaches students through `CasGatedQuestionPersister`. Concept extraction must run *before or during* that single-writer path, not as a side-channel.
- **ADR-0039**: BKT parameters per-concept can carry domain-level slip/guess overrides (`QuestionDocument.BktSlip / BktGuess / BktLearning`), and the gate-locked Koedinger defaults still apply per-skill. A multi-concept question doesn't change BKT params; it changes *which row* the update lands on.
- **ADR-0050**: Mastery is keyed on `(student, examTarget, skill)`. Concept extraction is per-question and exam-target-agnostic; the exam-target binding happens later when the question is selected for a session. Extraction should NOT carry an exam-target dimension.
- **ADR-0059 §15**: Bagrut reference items are wrapped in `Reference<T>`, never `Deliverable<T>`, and Reference items have no answer affordances. **Reference items should not produce ConceptAttempted events** (the student didn't answer them). Concept extraction still applies — to the *recreated variant*, not the reference. The variant inherits the reference's concept set as a starting point for extraction, then extraction re-runs on the variant text.
- **Ship-gate banned terms** (`docs/engineering/shipgate.md`, ADR-0048): mastery surfaces must not show streak counts or loss-aversion framing. Concept-vector visualisations on the student side are fine ("3 of 4 concepts confirmed by your work on this question") as long as they don't dress up missed concepts as a punishment.

### 2.4 What mastery tracking actually needs from extraction (the requirements)

For the BKT engine to update correctly:
1. **Stable, closed-set concept ids.** A concept id from January must mean the same skill in May. Recommendation: use `SkillCode` as the canonical id surface; the bagrut-taxonomy.json `conceptId` (`ALG-001`) becomes a synonym table for backward compat with seeded data.
2. **Consistent granularity across questions.** If question A is tagged `derivative-of-polynomial` and question B is tagged `calculus.derivative_rules`, the BKT can't fold them into the same row. Pick the granularity tier and stick to it (recommendation: taxonomy leaves for v1).
3. **Traceable ownership.** Every concept tag on a question must record: who/what produced it (LLM-extractor-v1 / curator-confirmed / curator-edited / rule-only), when, with what confidence. Without this, you can't roll back a bad extractor pass.
4. **Auditable extraction.** When a student's mastery report shows "concept X went up", the team must be able to trace back to "because question Q was tagged with X on date D by extractor E with confidence C, and the curator confirmed on date D'". This is the same audit pattern ADR-0032 §14 already enforces for CAS overrides.
5. **Bounded cardinality per question.** Pre-cap at ~5 concepts per question. A Bagrut Part-B problem realistically tests 3–5 concepts; an extraction that returns 12 is hallucination. The cap defends mastery posteriors against fan-out errors.
6. **Rejection path for low-confidence extractions.** If extraction returns nothing or low-confidence-only, the question gets tagged `unlinked` and stays out of the adaptive scheduler's frontier-driving loop until a curator sets it. Same pattern `LinkConcepts` already uses.

---

## Iteration 3 — Concrete plan

### 3.1 Recommended design (opinionated, single track)

**Hybrid extraction at variant-generate time (D1c + D4b), taxonomy-leaf granularity (D2a), primary-plus-supporting semantics (D3c), event-sourced + projected (D5c+a), curator-confirms-before-first-publish for the calibration corpus then auto-with-override (D6b→D6c).**

In one paragraph: the LLM call we already make to *recreate* a Bagrut variant is extended to also emit a structured `concepts: [{skill, role: primary|supporting, rationale}]` field; that field is canonicalized against the closed `SkillCode` set produced from `bagrut-taxonomy.json`; SymPy is asked to solve the recreated answer and the resulting *method trace* (new sidecar return field) is used to falsify or strengthen the LLM claims; the result is persisted as a `QuestionConceptsExtracted_V1` event and projected onto `QuestionDocument.ConceptIds` (multi) plus a `PrimaryConceptId` (single, == today's `ConceptId` for back-compat). At student-attempt time, `ConceptAttempted_V3` continues to fire on the primary concept (BKT semantics unchanged); supporting concepts receive a `MasterySignalEmitted_V1` nudge per attempt outcome (positive on correct, no-op on wrong — never negative, per ADR-0003 spirit). For the first 200 Bagrut items extracted under the new pipeline, the curator must confirm the concept set before publish; after that calibration corpus, the publish gate auto-confirms but the curator UI surfaces extractions for override.

Why this design (the pushable points):
- **It changes BKT semantics minimally.** Today's `ConceptAttempted_V3 { ConceptId }` keeps firing. We don't have to re-derive the posterior math for multi-skill updates. The supporting-concept channel reuses `MasterySignalEmitted_V1`, which is already designed to be a small additive signal that can't by itself cross a mastery threshold.
- **It folds extraction cost into a call we already make.** Variant generation already prompts Claude with the reference text. Adding a `concepts: []` JSON field to the prompt's structured output is ~5% additional output tokens — measured cost ≈ $0.0001/item at Haiku tier-2 (per ADR-0026), $0.001/item at Sonnet tier-3.
- **It uses SymPy as a falsifier, which is the ADR-0002 spirit.** LLM hallucinated concepts ("this question tests Lagrange multipliers" on a basic chain-rule problem) get caught when the SymPy method trace doesn't include them. This is the single biggest mitigation against the "LLM pollutes mastery" risk in §3.4.
- **It keeps the closed-set discipline.** Free-form concept strings ("derivative of compound trig with chain rule") would silently fork into incompatible ids. The canonicalizer is the gate.

### 3.2 File-level plan

**New files** (eight, four of them tests):

1. `src/shared/Cena.Infrastructure/Documents/QuestionConceptsDocument.cs` — projection-friendly view: `{Id, QuestionId, PrimaryConceptId, ConceptTags: List<ConceptTag>, ExtractorVersion, ExtractedAt, ConfirmedBy, ConfirmedAt}`. `ConceptTag = (SkillCode, Role, Confidence, Source, Rationale)`.
2. `src/actors/Cena.Actors/QuestionBank/Concepts/QuestionConceptsExtracted_V1.cs` — the event. Fields: `QuestionId, PrimaryConceptId, ConceptTags, ExtractorVersion, ExtractedAt, Source` (`llm_haiku_v1` | `rules_v1` | `curator_v1` | `sympy_method_trace_v1`).
3. `src/actors/Cena.Actors/QuestionBank/Concepts/QuestionConceptsConfirmed_V1.cs` — event emitted on curator confirm or override. Carries the final tag set + curator id.
4. `src/api/Cena.Admin.Api/Concepts/IConceptExtractor.cs` + `LlmConceptExtractor.cs` + `RuleConceptExtractor.cs` + `HybridConceptExtractor.cs` — the extraction surface. Hybrid composes Rule (always runs first) + Llm (runs if Rule has <2 hits OR primary confidence <0.55).
5. `src/api/Cena.Admin.Api/Concepts/ConceptCanonicalizer.cs` — maps free-form LLM strings to closed `SkillCode` set. Loads from `bagrut-taxonomy.json` at construction. Rejects unknown ids. Logs `[CONCEPT_CANONICALIZE_REJECT]` on miss.
6. `src/api/Cena.Admin.Api/Concepts/ConceptCuratorEndpoints.cs` — `GET /api/admin/questions/{id}/concepts`, `POST /api/admin/questions/{id}/concepts/confirm`, `POST /api/admin/questions/{id}/concepts/override`. All curator-gated; override requires `reason` + `ticketRef` (mirrors ADR-0032 §14 CAS-override pattern).
7. Tests: `ConceptCanonicalizerTests.cs` (closed-set discipline + rejection logging), `LlmConceptExtractorTests.cs` (mock Anthropic, verify JSON schema discipline, hallucination-rejection), `HybridConceptExtractorTests.cs` (rule-fallback paths), `ConceptExtractionArchitectureTest.cs` (only `CasGatedQuestionPersister` + `ConceptCuratorEndpoints` may emit `QuestionConceptsExtracted_V1` / `QuestionConceptsConfirmed_V1`).

**Edited files** (eight):

1. `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs` — add `List<string>? ConceptIds` (multi), add `string? PrimaryConceptId` (kept in sync with `ConceptId` via projection); old `ConceptId` field stays — it's hot-path-read by BKT and by `MartenQuestionPool`.
2. `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` — register `QuestionConceptsExtracted_V1` + `QuestionConceptsConfirmed_V1` event types and the `QuestionConceptsProjection` that folds onto `QuestionConceptsDocument` and updates `QuestionDocument.ConceptIds + PrimaryConceptId`.
3. `src/actors/Cena.Actors/Cas/CasContracts.cs` + `Cas/SymPySidecarClient.cs` — add `MethodTrace : List<string>?` to `CasVerifyResult` (e.g. `["polynomial_derivative", "set_to_zero", "factor", "second_derivative_test"]`). Sidecar Python adds method-trace emission on `Operation = Solve`.
4. `src/api/Cena.Admin.Api/Ingestion/GenerateVariantsJobStrategy.cs` — after CAS verify succeeds, call `IConceptExtractor.ExtractAsync(stem, latex, casMethodTrace)` and pass the result into the persister.
5. `src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs` — accept a new `QuestionConceptExtraction?` parameter on the session-aware persist overload; when present, append `QuestionConceptsExtracted_V1` in the same session as the question creation event.
6. `src/api/Cena.Admin.Api/Ingestion/BagrutDraftPersistence.cs:ClassifyTaxonomy` — keep, but downgrade comment to "rule-tier of HybridConceptExtractor"; the standalone single-leaf result keeps feeding the kanban for curator preview.
7. `src/actors/Cena.Actors/Mastery/BktTracer.cs` — no change to BKT update math. Add a sibling listener that consumes the supporting-concepts list from the question document and emits `MasterySignalEmitted_V1` per supporting concept on a correct attempt only. This file gets a 30-line addition, not a rewrite.
8. `src/admin/full-version/src/pages/apps/...` — admin SPA gets a "Concepts" panel on the curator question-review screen showing `[primary] [supporting × N]` with confidence bars, source badge (LLM / rule / SymPy / curator), inline override.

### 3.3 Phasing

**MVP (production-grade engine, behind ingestion-only flag)**:
- New event types + projection + `QuestionConceptsDocument`.
- `HybridConceptExtractor` wired into `GenerateVariantsJobStrategy`.
- SymPy method-trace return field (Python sidecar change + .NET contract).
- Curator confirm/override endpoints + admin SPA panel.
- BKT path unchanged (still primary-only). Supporting-concept signal NOT yet feeding mastery.
- Calibration corpus: first 200 Bagrut items extracted under new flow require curator confirm before publish. Track precision/recall against curator overrides.

**Phase 2 (turn on the secondary channel)**:
- `MasterySignalEmitted_V1` nudges fire on supporting concepts (positive-only, default delta 0.025, half the existing post-reflection delta — supporting evidence is weaker than the primary).
- Student-side mastery view shows multi-concept attribution per attempt ("This question moved your *derivative-of-product* and reinforced *chain-rule*").
- After 200-item calibration ships clean (>85% extractor precision against curator), flip the publish gate from "curator-must-confirm" to "curator-can-override".

**Phase 3 (post-launch — explicitly deferred)**:
- Finer "skill atom" granularity below taxonomy leaves.
- Re-extract pass on existing question bank (taxonomy version bump).
- LO ↔ ConceptIds bidirectional reconciliation.
- Per-domain extractor calibration (separate Bagrut Calculus extractor vs Probability extractor).

### 3.4 Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| **LLM hallucinates concepts not in the question** ("Lagrange multipliers" on a basic chain-rule problem) → false posterior gains across the cohort. | High | (a) Closed-set canonicalizer rejects unknown ids; (b) SymPy method-trace falsifier — claimed concepts that don't appear in the canonical solve trace get downweighted to supporting role at most; (c) calibration corpus + curator-confirm gate for first 200 items; (d) cap of 5 concepts per question. |
| **Concept-id drift** between bagrut-taxonomy.json id (`ALG-001`) and `SkillCode` slug (`math.algebra.quadratic-equations`). | High | A single source-of-truth synonym table generated at build time from `bagrut-taxonomy.json`; arch test fails the build if the same skill is referenced under both schemes inconsistently. Both forms keep working as input; canonicalizer always emits the `SkillCode` form. |
| **Taxonomy version bump invalidates old extractions.** | Medium | Persisted `ExtractorVersion` + `TaxonomyVersion` per row; on bump, a backfill job re-runs extraction; old rows kept until backfill confirmed. |
| **Curator-confirm queue backs up** and ingestion stalls. | Medium | Calibration window is bounded (200 items, ~2 weeks at current ingestion rate). After window, publish gate auto-confirms. SLA: curator queue <50 items at any time; alert if breached. |
| **Cost from LLM extraction running on every variant generation.** | Low (now), Medium (at scale) | Folded into the existing variant-generation LLM call (same prompt, extended JSON schema, ~5% more output tokens). At Haiku tier-2 the marginal cost is **~$0.0001 per question**; at 10k questions/yr that's **~$1/yr**. Even at Sonnet tier-3 it's ~$10/yr. Per memory `feedback_remind_costs`: this is rounding error vs. the existing $200–$2,000/mo Anthropic budget called out in `docs/ops/peripheral-costs.md`. |
| **SymPy sidecar method-trace is fragile** (different code paths for the same problem). | Medium | Use it as a falsifier (downweight, never override), not as the primary extractor. The hybrid extractor still produces a result if SymPy returns no trace. Conformance suite (`CasConformanceSuiteRunner`, ADR-0032) gets a method-trace section: pinned method traces for ~50 canonical Bagrut problems. CI fails on drift. |
| **Mastery posterior pollution if extraction runs on already-published questions.** | High if naive, Low if gated | Extraction runs ONLY at variant-generate time / curator-confirm time, not on existing published questions. Backfill is a separate, opt-in flag and fires `QuestionConceptsExtracted_V1` events that go through the same curator-confirm gate. Existing students' BKT rows are not touched until a curator confirms the backfill batch. |
| **Mismatch between primary-concept choice and the BKT row that already exists** for a question. | Medium | The first ever `QuestionConceptsConfirmed_V1` event for a question pins `PrimaryConceptId == QuestionDocument.ConceptId` (legacy) by default; curator can override but the override fires a `QuestionPrimaryConceptChanged_V1` event that the BKT projection treats as a structural rename, not a fresh observation. |

### 3.5 What the user surface shows (briefly — just enough to anchor the design)

Admin curator review (one question):
> **Concepts detected** *(LLM-Haiku-v1 + SymPy method-trace, 2026-05-01)*
> - `math.calculus.derivative-rules` — **primary**, conf 0.92, supported by SymPy trace `polynomial_derivative`
> - `math.calculus.applications-of-derivatives` — supporting, conf 0.78
> - `math.functions.function-basics` — supporting, conf 0.55
> [Confirm all] [Edit] [Reject extraction → curator-pick]

Student post-attempt (after Phase 2 turns on the supporting channel):
> "Nice — your work on this confirmed *Derivative rules*. We also saw partial evidence on *Applications of derivatives*."
> (No streak. No XP. No "you're on fire". Per ship-gate.)

---

## Recommendation

Adopt the hybrid (rules+LLM) extractor that runs at variant-generate time, persists multi-concept tags via a `QuestionConceptsExtracted_V1` event projected onto `QuestionDocument.ConceptIds + PrimaryConceptId`, and uses a new SymPy method-trace return as a falsifier. Keep BKT semantics unchanged in MVP — `ConceptAttempted_V3` keeps firing on the primary concept only — and turn on supporting-concept `MasterySignalEmitted_V1` nudges in Phase 2 once a 200-item calibration corpus has been curator-confirmed and extractor precision is measured ≥85%. The closed-set canonicalizer (taxonomy-leaf granularity, generated from `bagrut-taxonomy.json` at build time) is the load-bearing discipline; without it, free-form LLM strings will silently fork the concept catalog and pollute the mastery posteriors. Curator confirm-before-publish for the calibration window is the production-grade gate (per memory `feedback_no_stubs_production_grade`); after the window, publish auto-confirms but the override surface stays. Cost is negligible (~$1–$10/yr at current question volumes — folded into the existing variant-generation LLM call). The two specific calls the user should push back on if they disagree: (a) primary-only BKT in MVP rather than weighted multi-concept BKT — chosen because re-deriving BKT posterior math for fractional updates is a much larger and riskier change than reusing the existing `MasterySignalEmitted_V1` channel; (b) taxonomy-leaf granularity rather than finer skill atoms — chosen because the existing `SkillCode` and bagrut-taxonomy.json infrastructure is already wired to this granularity, so this gives us a working multi-concept system in days rather than weeks. Both are reversible in Phase 3 without reverting any of the MVP plumbing.
