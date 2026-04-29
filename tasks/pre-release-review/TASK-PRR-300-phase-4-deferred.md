# TASK-PRR-300: Analytics dashboard for OpenTelemetry mock-exam counters

**Priority**: low
**Effort**: M (most are 1-3 days)
**Source docs**: claude-code Phase 4 honest-gap audit 2026-04-29 (mock-exam runner final list).
**Assignee hint**: unassigned
**Tags**: source=exam-prep-phase-4-deferred,epic=epic-prr-n,ops
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Close the ops gap from Phase 4 of the mock-exam runner audit — these are the items I called out honestly as still missing from the production-grade ship.

## Scope

Item-level scope is defined by the title. The runner is production-shipped at the contract layer; this task closes the named feature/quality gap.

## DoD

Item is feature-complete + tested + documented + landed on origin/main.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<commit sha>"`
