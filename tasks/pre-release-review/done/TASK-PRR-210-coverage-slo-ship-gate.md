# TASK-PRR-210: Coverage SLO ship-gate — ≥N parametric variants per rung, CI-enforced

**Priority**: P0 — ship-blocker (the "100% coverage" claim lives or dies here)
**Effort**: S — 2-3 days
**Lens consensus**: persona-educator, persona-ministry, persona-enterprise, persona-sre
**Source docs**: Epic PRR-E §Coverage definition
**Assignee hint**: claude-subagent-shipgate (CI)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=educator+ministry+enterprise
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Author the coverage SLO, ship a CI check that fails when any cell in the Epic's coverage matrix has < N ready CAS-verified variants, and wire the gate into the release workflow. Without this gate, "full production-ready coverage" is a claim; with it, it is a release blocker.

## Files

- `ops/slo/coverage-rung-slo.md` (new) — documents N per (difficulty × question_type) rung
- `scripts/shipgate/coverage-rung-check.mjs` (new) — CI script
- `.github/workflows/shipgate.yml` — add step invoking the script
- `ops/reports/coverage-rung-status.md` (auto-generated on every CI run)

## Non-negotiable references

- prr-201 (waterfall + projection) is the data source.
- ADR-0043 (Bagrut reference-only) — variants similar to Ministry corpus are already dropped by prr-201 and do not count toward SLO.

## Definition of Done

- `ops/slo/coverage-rung-slo.md` defines N values. Proposed default (subject to persona-educator review):
  - MCQ cells: N = 10 (deep buffer for exposure control per engine doc §25).
  - Step-solver cells: N = 5 (authoring cost higher; buffer smaller).
  - FBD-construct cells: N = 3 (narrow topic subset; still enforced).
- Script reads the coverage projection read model, emits `ops/reports/coverage-rung-status.md` with per-cell status.
- Exit 0 when every cell in the currently-claimed release scope (track × methodology selected by a release tag) meets N.
- Exit non-zero with a clear message listing the failing cells when any cell is under.
- CI failure surface: PRs fail on coverage regression (a cell that was green falls below N); nightly main build fails and alerts when release tag scope is under.
- `ops/reports/coverage-rung-status.md` committed by the CI job so reviewers see it in PRs.
- Tested: a contrived projection state with 1 red cell must produce an exit 1; with all cells green must produce exit 0.
- Runbook entry: how to hotfix a red cell before a release (short-term: curator-task escalation; long-term: author new template).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-shipgate --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-educator**: N values per rung reviewed by educator persona — 10/5/3 is the opening position, adjust with evidence. Owned here.
- **persona-ministry**: release tag scope must include all 4 (methodology × track) quadrants for Bagrut claim. A release that omits one quadrant must label itself accordingly (marketing-copy scope, not this task's code scope, but the gate surfaces it).
- **persona-enterprise**: SLO is global (content corpus is shared); per-tenant rollouts inherit the global SLO.
- **persona-sre**: gate runs against the projection, not a live rebuild — SLO check is O(1) read per cell; full run < 30s.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-201 (projection)
- Adjacent: prr-209 (admin surface of the same data)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
