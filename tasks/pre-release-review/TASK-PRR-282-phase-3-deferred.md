# TASK-PRR-282: Tenant-scoped /api/me/exam-prep/feature-flags read

**Priority**: low
**Effort**: S-M
**Source docs**: claude-code Phase 3 self-audit 2026-04-29 (mock-exam runner SHOULD-FILE list).
**Assignee hint**: unassigned
**Tags**: source=exam-prep-phase-3-deferred,epic=epic-prr-n,backend
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Close the backend gap from Phase 3 of the mock-exam runner audit.

## Scope

Item-level scope is defined by the title. The runner is production-shipped; this task closes the named SHOULD-FILE gap.

## DoD

Item is feature-complete + tested + documented + landed on origin/main.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<commit sha>"`
