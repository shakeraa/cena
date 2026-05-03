# ADR-0062 — Concept extraction and multi-skill mastery (MVP)

* **Status**: Accepted (2026-05-03)
* **Supersedes**: none. Extends ADR-0002 (SymPy CAS oracle), ADR-0032 (CAS-gated question ingestion), ADR-0039 (BKT parameters and fading), ADR-0050 (multi-target student exam plan), ADR-0059 §15 (Bagrut reference variant generation).
* **Owner**: claude-code (coordinator)
* **Related research**:
  * `docs/design/concept-extraction-mastery-001-research.md` — single-pass design synthesis (3 iterations)
  * `docs/design/concept-extraction-mastery-002-multi-persona-deep-research.md` — 10-iteration multi-persona deep research with 39 cited references

## Context

Today every Cena math question carries exactly **one** concept tag (`QuestionDocument.ConceptId`). At student attempt time, `ConceptAttempted_V3` fires with that one id and BKT folds the result into exactly one mastery row. Real Bagrut Part-B problems test 3–5 concepts; the platform's central pedagogical claim ("we know what you know") therefore systematically under-tracks the supporting concepts.

The 002 multi-persona research validated this gap with citations from Corbett & Anderson, Koedinger, Pavlik PFA, EdNet KT1, ALEKS, MATHia, ASSISTments, and the LLM-tagging literature.

## Decision

Adopt the recommendation from the 001 research, validated by the 002 deep research, with one tightening:

1. **Extraction mechanism**: hybrid — rules-tier first (existing keyword classifier as scaffolding), LLM-tier (Anthropic Haiku per ADR-0026) when rules return <2 hits or low confidence. Fold the LLM call into the existing variant-generation pass so there is no extra round trip.
2. **Granularity**: taxonomy leaves only. Use the ~73 leaves already published in `scripts/bagrut-taxonomy.json` as the closed `SkillCode` set. Defer finer "skill atoms" until evidence shows leaves are too coarse.
3. **Multi-concept semantics**: primary + supporting roles.
   * BKT continues to fire on `PrimaryConceptId` only — `ConceptAttempted_V3` is unchanged.
   * Supporting concepts get a `MasterySignalEmitted_V1` nudge (Phase 2 only, half post-reflection delta, positive-only, never decrements).
4. **Storage**: event-sourced + projected.
   * New event `QuestionConceptsExtracted_V1` (extractor output)
   * New event `QuestionConceptsConfirmed_V1` (curator-confirmed override)
   * Projected by `QuestionListProjection.Apply(QuestionConceptsExtracted_V1)` and `Apply(QuestionConceptsConfirmed_V1)` onto `QuestionReadModel.Concepts : List<string>` (the list-view document Marten serves to the curator UI and downstream readers). The `QuestionState` aggregate rebuilds the same set on `AggregateStreamAsync<QuestionState>` into `QuestionState.ConceptIds`. Last-write-wins: a confirm event overwrites a prior extraction, and a later extraction (e.g. operator re-run) quietly overwrites a confirm — Phase 2 will add a "frozen" bit on confirm if telemetry shows curators want sticky confirms.
   * The legacy single-string `QuestionDocument.ConceptId` is unchanged — it remains the BKT primary-key consumed by `ConceptAttempted_V3` and the existing student-session read path. Concept events do NOT update `QuestionDocument` directly.
5. **Curator validation gate**:
   * First 200 Bagrut items extracted under the new pipeline: curator must confirm the concept set before publish (calibration corpus).
   * After 200: extraction stands by default; curator UI surfaces the set for one-click override.
6. **Concept-id stability**: stable IDs are non-negotiable for BKT. The closed-set canonicalizer maps free-form LLM output → existing `SkillCode`. Any unmappable suggestion falls back to `unlinked` (same pattern `ContentExtractorService.LinkConcepts` already uses).
7. **Phase 2 precondition gate (the tightening from 002 deep research)**:
   * Supporting-concept `MasterySignalEmitted_V1` nudges fire ONLY for leaves where `≥10` items have been published with that concept attached.
   * The threshold matches the published BKT identifiability floor (van de Sande 2013, Beck & Chang 2007).
   * Below the floor, posteriors are too noisy to defend; the gate prevents the system from over-claiming early.

## Constraints honored

* **ADR-0002 (SymPy oracle)**: extraction is metadata, not correctness. SymPy gets a new method-trace return field used to falsify low-confidence concept claims (Phase 1.5).
* **ADR-0003 (misconception session scope)**: concept tags on questions are NOT misconceptions. The two channels stay separate.
* **ADR-0032 (CAS-gated ingestion)**: extraction runs at the variant-generate layer, before the single-writer `CasGatedQuestionPersister` path.
* **ADR-0039 (BKT parameters)**: BKT math is unchanged. Koedinger defaults still apply per-skill. The supporting-concept nudge channel uses `MasterySignalEmitted_V1` (already-defined, single-skill, single-delta) — not a new BKT path.
* **ADR-0050 (exam-target keying)**: extraction is per-question, exam-target-agnostic.
* **ADR-0059 §15 (Bagrut reference-only)**: extraction runs on the recreated variant, not on the reference. The variant inherits the reference's seed concept set.
* **Ship-gate banned terms**: mastery surfaces must not show streak counts or loss-aversion framing.

## Phasing

* **Phase 0** — foundation (this ADR)
  * BagrutTaxonomyCatalog (closed-set canonicalizer)
  * QuestionConceptsExtracted_V1, QuestionConceptsConfirmed_V1 event types + Marten registration
  * QuestionListProjection writes the concept set onto `QuestionReadModel.Concepts`; `QuestionState` rebuilds `ConceptIds` on aggregate replay
  * Unit tests
* **Phase 1** — extraction-only (no BKT change)
  * Variant-generation prompt extended to emit `concepts: [{skill, role, rationale}]`
  * Canonicalizer + persistence
  * Curator confirm UI
* **Phase 1.5** — falsifier
  * SymPy method-trace return field
  * Concept claims unsupported by method-trace are downweighted
* **Phase 2** — supporting-concept nudges
  * `MasterySignalEmitted_V1` channel for supporting concepts
  * `≥10 items per leaf` precondition gate (the 002 tightening)
* **Phase 3** — re-evaluate
  * Telemetry-driven decision: do leaves provide actionable mastery, or do we need finer atoms?
  * Telemetry-driven decision: is the small Phase-2 nudge strong enough, or do we need weighted multi-concept BKT?

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| LLM hallucinates concepts not in the taxonomy | Closed-set canonicalizer rejects → falls to `unlinked`; never silently accepts |
| Extractor precision below ~85% pollutes mastery | Curator confirm gate for first 200 items as calibration corpus; precision measured against curator overrides; Phase 2 doesn't activate until measured |
| Taxonomy drift over time | `SkillCode` is normalized, validated; renames go through a planned migration event, not silent |
| BKT posterior instability at low items-per-skill | `≥10 items per leaf` precondition gate (Phase 2) |
| Cost runaway from LLM extraction | Folded into existing variant-generation call; marginal cost ~$0.04–$0.45/year at expected volume per Anthropic 2026 pricing |

## Cost (verified)

At Anthropic 2026 pricing (Haiku 4.5: $1/MTok in, $5/MTok out; Sonnet 4.6: $3/$15) and ~250 tokens per extraction:

* 5,000 items/year on Haiku: ~$0.04–$0.13/year
* 5,000 items/year on Sonnet: ~$0.45/year

Cost is a rounding error — not a deciding factor.

## Decision drivers (002 research findings, ranked)

1. Real production cognitive tutors (MATHia, ALEKS, Cognitive Tutor Algebra) use single-skill-per-step KCs by construction, not weighted multi-skill BKT (refutes the "weighted is the modern way" intuition).
2. EdNet KT1 ships at 188 skills over 13,169 questions — Cena's ~73-leaf taxonomy is in the right order of magnitude for an MVP (refutes the "only ALEKS-scale catalogs work" intuition).
3. LLM tagging accuracy at ~24-concept label sets reaches F1 ≈ 81.75 vs human 88.51 (Hao et al. 2024); accuracy degrades sharply with label-set size unless taxonomy-guided.
4. ≥10 items per skill is the published informal floor for stable BKT (van de Sande 2013).

## Open questions (deferred to Phase 3)

* Should `unlinked` items be excluded from CAT/PSI scheduling entirely, or surfaced as "needs concept review"?
* Should curator-edited concept sets emit a separate `QuestionConceptsCuratorOverride_V1` event for audit, or extend `QuestionConceptsConfirmed_V1` with an `editType` field? (currently planned: extend the existing event.)
* Method-trace falsifier weight — additive, multiplicative, or veto?

## Implementation drift (2026-05-03)

Phase 0 originally planned to widen `QuestionDocument` with `PrimaryConceptId : string` and `ConceptIds : List<string>` (see commit `9a827ba1` and the original wording of §4 above). The fields were declared on the type but no production writer was wired — the projection landed on `QuestionReadModel.Concepts` instead, and the architect-review §gap-3 caught the gap. Rather than wire a second writer, the decision was to keep the projection on `QuestionReadModel` (the live list-view document Marten serves to consumers) and treat the `QuestionDocument` widening as superseded.

Net effect:

* `QuestionDocument.ConceptId` (single string, legacy) — still authoritative for BKT primary-key lookup; unchanged.
* `QuestionDocument.PrimaryConceptId` and `QuestionDocument.ConceptIds` — declared but unused by any production writer; effectively dead. A future cleanup pass should either delete them or wire a writer; this ADR does not require either.
* `QuestionReadModel.Concepts` — populated by `QuestionListProjection` from the V1 events; this is what `MartenQuestionPool` and the curator UI read.
* `QuestionState.ConceptIds` — rebuilt on aggregate replay; this is what command handlers read.

Future-self reading this ADR: the widening of `QuestionDocument` was planned but never shipped because the second writer is unnecessary. The single source of truth is the event stream; the projection writes one read model and the aggregate rebuilds the other.
