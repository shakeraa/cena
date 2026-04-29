# TASK-PRR-284: Coverage SLO ship-gate variant-flood detection (G9; defense-in-depth on PRR-272)

**Priority**: P1 — defense-in-depth on PRR-272's parametric-doesn't-count rule
**Effort**: S (2-3 days; CoverageTargetManifest extension + ship-gate config + test)
**Lens consensus**: claude-5 self-audit 2026-04-29 G9; persona-finops §14.2 (variant-flood cost-side analysis)
**Source docs**: claude-5 self-audit 2026-04-29 G9; PRR-272 (parametric-doesn't-count rule); [contracts/coverage/coverage-targets.yml](../../contracts/coverage/coverage-targets.yml); [src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs](../../src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs)
**Assignee hint**: backend (Question-Bank context); coordinate with PRR-272 owner
**Tags**: source=claude-5-audit-2026-04-29,epic=epic-prr-n,priority=p1,coverage-slo,ship-gate,backend
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

PRR-272 establishes that parametric variants don't count toward coverage. PRR-284 is **defense-in-depth**: even if some variants leak past the `coverage_eligible == false` filter (bug, projection-rebuild gap, or a future variant-kind addition), the coverage SLO ship-gate should still detect cell-saturation by a single source paper code.

Rule: **a coverage cell whose count is >50% derived from a single source paper code FAILS the ship-gate, regardless of `coverage_eligible` filter state.**

## Scope

### Manifest extension

1. **`CoverageTargetManifest` reads source-paper-code distribution per cell** in addition to the count. For each cell, compute `max_source_share = max(count_per_source_paper_code) / cell_count`.
2. **Ship-gate fails** when a cell's `max_source_share > 0.5` (>50% from one source). Configurable threshold (default 0.5) via `coverage-targets.yml`.
3. **Failure message**: `"Cell (topic={topic}, difficulty={difficulty}, methodology={methodology}, track={track}, questionType={qtype}): {cell_count} variants but {max_source_share*100}% derive from source paper code {top_source_code}. Variant-flood detected."`

### Tests

4. **Unit**: cell with 10 variants where 6 are from שאלון 035582 → ship-gate fails (60% > 50%).
5. **Unit**: cell with 10 variants spread across 5 source paper codes → ship-gate passes.
6. **E2E (synthetic)**: load fixture corpus with 30 variants from one source → ship-gate fails.

### Configuration

7. Add `defaults.variantFloodThreshold: 0.5` to `coverage-targets.yml` with comment explaining the rule + reference to PRR-284.
8. Document the rule + threshold in `docs/engineering/shipgate.md`.

## Files

### Modified
- `src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs` — add per-source distribution + threshold check.
- `contracts/coverage/coverage-targets.yml` — `defaults.variantFloodThreshold` config.
- `docs/engineering/shipgate.md` — document the rule.

### New
- `src/actors/Cena.Actors.Tests/QuestionBank/Coverage/VariantFloodDetectionTests.cs` (unit + integration).

## Definition of Done

- `max_source_share` computed per cell.
- Threshold-driven ship-gate failure.
- Tests cover green + red cases.
- Full `Cena.Actors.sln` build green.

## Blocking

- PRR-272 must land first (provides `coverage_eligible` filter as the primary defense; PRR-284 is defense-in-depth on top).

## Non-negotiable references

- PRR-272 (primary defense)
- persona-finops §14.2 (cost-side mirror)

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + threshold-config sha + test green output>"`
