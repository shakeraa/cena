# TASK-PRR-272: Parametric variants do NOT count toward coverage SLO (G1; ADR-0043 + ADR-0059 amendment)

**Priority**: P0 — coverage-SLO ship-gate honesty; gates PRR-245 launch
**Effort**: M (3-5 days; ADR amendments + projection field + reader filter + arch test + backfill)
**Lens consensus**: ADR-0043 reminder pinned at `contracts/coverage/coverage-targets.yml:21`; persona-ministry §14.2 (Israeli §16 derivative-works distance); claude-1 ACK on m_0485821c116e (no objection)
**Source docs**: claude-5 self-audit 2026-04-29 G1; [ADR-0043](../../docs/adr/0043-bagrut-reference-only-enforcement.md), [ADR-0059 §15.5](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md), [contracts/coverage/coverage-targets.yml](../../contracts/coverage/coverage-targets.yml), [src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs](../../src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs)
**Assignee hint**: backend (Question-Bank context); kimi-coder if she returns + heartbeats; otherwise claude-2 has resolver context from PRR-244
**Tags**: source=claude-5-audit-2026-04-29,epic=epic-prr-n,priority=p0,coverage-slo,adr-amendment,backend
**Status**: Ready
**Tier**: launch-adjacent (gates PRR-245 ship-gate honesty)
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

ADR-0043 establishes "only recreated CAS-verified variants count toward coverage." ADR-0059 §15.5 introduces variant generation in two flavors — parametric (deterministic parameter substitution) and structural (Tier-3 LLM scenario rewrite). The two flavors have **different coverage-counting consequences** that ADR-0059 §15.5 did not specify:

- A 30-student classroom drilling שאלון 035582 q3 with parametric variants fills a coverage cell with 30+ variants. `CoverageTargetManifest` reads cell green. Pedagogically the cell still has *one* underlying question. **Coverage SLO falsely passes via variant-inflation.**
- Persona-ministry §14.2 separately established that parametric variants are *more* legally exposed under Israeli Copyright Law §16 (derivative-works distance) — they retain more of the source than structural recreations.

Both lenses converge on the same rule: **parametric variants are derivative, not recreation. They MUST NOT count toward the coverage SLO.**

This task amends ADR-0043 §1 + ADR-0059 §15.5 to encode the rule, adds a `coverage_eligible` projection field, and updates `CoverageTargetManifest` to filter on it.

## Scope

### ADR amendments

1. **ADR-0043 §1 amendment** — add a normative paragraph: *"In ADR-0043's coverage-counting rule, 'recreated' means **structural** recreation (different scenario, same skill). Parametric variants (parameter substitution; same scenario, different numbers) are derivative under Israeli Copyright Law §16 and do NOT count toward coverage. See PRR-272 for projection-level enforcement."*
2. **ADR-0059 §15.5 amendment** — add a normative paragraph: *"Persisted variants carry `coverage_eligible: bool` derived from `VariationKind`: structural ⇒ true; parametric ⇒ false. `CoverageTargetManifest` reader filters cell counts on `coverage_eligible == true`. See PRR-272 for implementation."*

### Backend

3. **Add `coverage_eligible: bool` field** to the variant projection. Choose ONE of:
   - (a) Add to `BagrutCorpusItemDocument` if variants are persisted there.
   - (b) Add to `QuestionDocument` if variants are persisted as questions with a `SourceProvenance` lineage.
   Pick based on actual variant-persistence path; document in ADR-0059 §15.5 amendment.
4. **Initialize from `VariationKind`**: `coverage_eligible = (VariationKind == VariationKind.Structural)`. Default `coverage_eligible = true` for non-variant rows (TeacherAuthoredOriginal + AiRecreated whole-question).
5. **Update `CoverageTargetManifest` reader** to filter cell counts on `coverage_eligible == true`. Document the filter in the manifest's xmldoc.
6. **Backfill projection rebuild** for existing variants — run a one-shot Marten projection rebuild that sets `coverage_eligible` based on stored `VariationKind` (or defaults to true for null/legacy). Document in `docs/ops/migrations/` per the existing migration pattern.

### Tests

7. **Architecture test**: `CoverageEligibleEnforcedTest.cs` — parametric variants written to `BagrutCorpusItemDocument` / `QuestionDocument` MUST have `coverage_eligible == false`. Fails build if a code path sets `coverage_eligible = true` for a parametric persistence.
8. **Coverage matrix unit test**: 30 parametric variants of one source → cell count contribution = 0 (not 30). 1 structural variant of the same source → cell count contribution = 1.
9. **E2E**: variant-flood scenario (30 students drilling one question parametrically) does NOT inflate the coverage report.

### Queue housekeeping

10. PRR-262 (filed earlier for the same R13 territory) overlaps with PRR-271 — coordinate with claude-code on which to keep. PRR-272 (this task) is purely the parametric-doesn't-count rule, distinct from R13 (Haiku equivalence-check on structural variants).

## Files

### Modified
- `docs/adr/0043-bagrut-reference-only-enforcement.md` — §1 amendment.
- `docs/adr/0059-bagrut-reference-browse-and-variant-generation.md` — §15.5 amendment.
- `src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs` — reader filter.
- `src/shared/Cena.Infrastructure/Documents/BagrutCorpusItemDocument.cs` OR `QuestionDocument.cs` — add field.
- `src/actors/Cena.Actors.Tests/Architecture/CoverageEligibleEnforcedTest.cs` (new).

### New
- `docs/ops/migrations/2026-04-29-coverage-eligible-backfill.md` — migration runbook.

## Definition of Done

- ADR amendments merged.
- `coverage_eligible` field on variant projection; backfilled.
- `CoverageTargetManifest` filters on it.
- Arch test catches regressions.
- Variant-flood E2E proves coverage stays honest.
- Full `Cena.Actors.sln` build green; full test suite green (per PRR-255 / PRR-303 discipline).

## Blocking

- None at start. Coordination with PRR-262 / PRR-271 owners on R13 overlap.

## Non-negotiable references

- ADR-0043 §1, ADR-0059 §15.5, persona-ministry findings §14.2, persona-finops findings §14.2 (variant-flood cost-side mirror)
- Memory "No stubs — production grade"
- Memory "Verify data E2E"

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + ADR amendments shas + projection rebuild migration sha + arch test sha>"`
