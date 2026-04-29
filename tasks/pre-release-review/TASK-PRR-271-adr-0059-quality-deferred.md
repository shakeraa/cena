# TASK-PRR-271: ADR-0059 R13 Tier-2 Haiku second-pass equivalence-check on structural variants

**Priority**: P1
**Effort**: M (2-4 days)
**Source docs**: ADR-0059 §14.4, claude-code self-audit 2026-04-29 (deferred-task filing)
**Assignee hint**: unassigned
**Tags**: source=adr-0059-deferred,epic=epic-prr-n,priority=p1,quality
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Close the R13 Tier-2 Haiku second-pass equivalence-check on structural variants gap from ADR-0059 §14.4. The mock-exam runner (exam-prep slice) is shipping in parallel; this task is the wrapper-rollout side that gates flag-flip on legal sign-off (PRR-249).

## Scope

See ADR-0059 §14.4 for the R-item normative description. Implementation guidance:
- Backend services live under `src/actors/Cena.Actors/...` and `src/api/Cena.Admin.Api/...` per the bounded-context discipline (ADR-0001).
- Tests: unit (`Cena.Actors.Tests/`) + e2e-flow (`src/student/full-version/tests/e2e-flow/`).
- Negative-property assertions: GD-004 ship-gate scanner is the canonical banned-terms enforcer.

## DoD

Item is feature-complete + tested + documented + landed on origin/main behind the existing `Cena:Variants:BagrutSeedToLlmEnabled` flag (or its successor).

## Blocking

- PRR-245 wrapper merge for items that depend on the student-side reference library
- PRR-249 §6 legal sign-off for items that flip the flag on for production

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<commit sha>"`
