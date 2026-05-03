# RDY-019a: Bagrut Topic Taxonomy + Existing Question Remap

**Parent**: [RDY-019](tasks/readiness/RDY-019-bagrut-corpus-ingestion.md) (split 1 of 3)
**Priority**: Medium
**Complexity**: Mid engineer
**Effort**: ~1 week
**Blocker status**: None — can start immediately (no CAS-gate dependency)

## Problem

Concepts are implicit string IDs (e.g., `math_5u_derivatives_chain_rule`) with no formal hierarchy matching the official Ministry syllabus. Cannot answer "Have we covered all topics on the 5-unit exam?"

## Scope

### 1. Create canonical taxonomy

New file: `scripts/bagrut-taxonomy.json`

Shape:
```json
{
  "math_5u": {
    "algebra": ["equations", "inequalities", "polynomials", "sequences"],
    "calculus": ["limits", "derivatives", "integrals", "applications"],
    "geometry": ["euclidean", "analytic", "trigonometry"],
    "probability": ["combinatorics", "probability", "statistics"]
  },
  "math_4u": { ... },
  "math_3u": { ... }
}
```

Must be version-controlled and reviewed against Ministry syllabus reference.

### 2. Remap existing questions to taxonomy

- Every question currently in `QuestionBankSeedData` / Marten events must gain a `TaxonomyNode` field referencing a leaf in the JSON.
- Write a migration script that reads all published `QuestionDocument` events, resolves node via existing concept ID heuristics, and emits a `QuestionTaxonomyMapped_V1` event.
- Any unmappable question is flagged for manual review.

### 3. Validator

Add `scripts/taxonomy-validator.ts` (mirror `scripts/glossary-validator.ts` pattern from RDY-027):
- Parse JSON schema
- Enforce every leaf is unique
- Enforce every existing question maps to an extant node
- Wire into CI

## Files to Modify

- New: `scripts/bagrut-taxonomy.json`
- New: `scripts/taxonomy-validator.ts`
- New: `src/shared/Cena.Contracts/Events/QuestionTaxonomyMapped_V1.cs`
- Edit: `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` (add TaxonomyNode to seed data)
- New migration: `src/tools/Cena.Tools.TaxonomyMigrator/` (or script under `scripts/`)
- CI workflow: add taxonomy-validator step

## Acceptance Criteria

- [ ] `bagrut-taxonomy.json` exists with full 5u/4u/3u hierarchy
- [ ] All existing questions mapped; unmappable count is zero OR explicitly listed for review
- [ ] `QuestionTaxonomyMapped_V1` event registered in Marten
- [ ] Validator passes in CI
- [ ] Full `Cena.Actors.sln` builds with 0 errors

## Coordination notes

- Do NOT bypass `QuestionBankService` for seed writes — wait for kimi's `CAS-GATE-SEED-REFACTOR` (t_d995fe1da366) to land, OR coordinate with kimi so seed writer changes are done in one pass.
- No overlap with claude-1's RDY-034 CAS-gated ingestion work; taxonomy is orthogonal.
- Feeds RDY-019b (scraper ingestion) and RDY-019c (coverage report).

---- events ----
2026-04-15T16:31:44.191Z  enqueued   -
