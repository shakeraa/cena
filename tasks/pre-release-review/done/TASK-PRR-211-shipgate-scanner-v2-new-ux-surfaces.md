# TASK-PRR-211: Ship-gate scanner v2 — extend to `HintLadder`, `StepSolverCard`, `Sidekick` surfaces

**Priority**: P1
**Effort**: S — 2 days
**Lens consensus**: persona-ethics, persona-cogsci, persona-a11y, persona-finops
**Source docs**: `docs/engineering/shipgate.md`, EPIC-PRR-D (shipgate v2), EPIC-PRR-E surfaces
**Assignee hint**: claude-subagent-shipgate (CI)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=ethics+cogsci+a11y
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Extend the banned-vocabulary DOM scanner to cover the new UX surfaces shipped by this epic — `HintLadder`, `StepSolverCard`, `FreeBodyDiagramConstruct`, `SidekickDrawer` — AND add the `no-LLM-import-in-L1` source-level rule for the hint-ladder backend. Ensures the surfaces this epic adds can't drift into dark-pattern territory post-launch.

## Files

- `scripts/shipgate/banned-vocab-scanner.mjs` — extend scan patterns
- `scripts/shipgate/l1-no-llm-import-scanner.mjs` (new)
- `.github/workflows/shipgate.yml` — add steps
- `docs/engineering/shipgate.md` — document the extended scope
- `tests/shipgate/fixtures/` — add positive + negative fixtures for each new surface

## Non-negotiable references

- EPIC-PRR-D (shipgate v2) — this task extends, does not duplicate.
- ADR-0045 — the "L1 must not import LLM" rule originates here and becomes CI-enforced.

## Definition of Done

- DOM scanner covers the 4 new surfaces listed above.
- Banned patterns expanded with surface-specific lexicon:
  - HintLadder: no "Hint 1 of 5", no "1 remaining", no "reveal XP".
  - StepSolverCard: no countdown/timer pressure copy ("time is running out"), no percentile comparisons.
  - Sidekick: no "streak", no "daily quota", no "catch up", no loss-aversion framing.
  - FBD: no "chance to score", no variable-ratio reward language.
- Source-level `l1-no-llm-import-scanner.mjs` fails the build when any file matched by `**/L1*HintGenerator.cs` imports LLM packages.
- CI workflow invokes both scanners and aggregates results.
- Fixtures: ≥3 positive fixtures and ≥3 negative fixtures per surface. Scanner exercise must catch each positive and pass each negative.
- Documentation: the extended lexicon is spelled out in `docs/engineering/shipgate.md`.
- Runs in < 30s on full repo.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-shipgate --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-ethics**: lexicon authored here; extended as new dark-pattern forms surface in persona reviews.
- **persona-cogsci**: expertise-reversal-defeating copy (e.g., forced tutorials for advanced students) added to lexicon.
- **persona-a11y**: color-alone indicators flagged as a lint fail (extends Axe CI — this is DOM lint, not full a11y audit).
- **persona-finops**: L1-no-LLM-import scanner owned here.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Adjacent: EPIC-PRR-D (shipgate v2 base)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
