# TASK-PRR-264: Investigate queue DB integrity — PRR-247 task ID disappearance

**Priority**: P2 — data integrity; coordinator dismissed during 2026-04-28 session
**Effort**: XS (1-2 hours; investigation)
**Source docs**: claude-code self-audit 2026-04-28
**Assignee hint**: claude-code or whoever owns `.agentdb/kimi-queue.js` + better-sqlite3 schema
**Tags**: source=claude-code-audit-2026-04-28,priority=p2,queue,db-integrity
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: (no epic; queue infrastructure)

---

## Goal

During the 2026-04-28 PRR-247 work, the task ID `t_47ee2d95bcd0` (PRR-247: ADR-0060 SessionMode wiring) was successfully claimed by `claude-code` at 17:00:18 (per session log), but `node .agentdb/kimi-queue.js complete t_47ee2d95bcd0` returned `not found: t_47ee2d95bcd0` after the merge. `node ... show t_47ee2d95bcd0` and `node ... list --status all | grep PRR-247` both returned empty. The task was code-merged but its queue row vanished.

Coordinator shrugged this off as housekeeping. It's a data-integrity signal — either the row was deleted by someone else (who? when?) or there's a bug in the claim → in-progress → complete path. Investigate.

## Scope

1. Inspect `.agentdb/kimi-queue.db` SQLite schema for `tasks` + `task_events` tables.
2. Search for any audit / event row referencing `t_47ee2d95bcd0` — was a delete event recorded?
3. Reproduce: claim a fresh task from `claude-code`, check for race conditions in the queue's atomic-claim path. Are there scenarios where a task can be evicted between claim + complete?
4. If the issue is reproducible, file the actual cause (race / GC / bug) and ship a fix in `.agentdb/kimi-queue.js`.
5. If not reproducible, document the investigation + add a heartbeat-or-log trigger so a future occurrence leaves a forensic trace.

## Definition of Done

- Root cause documented (or proof of unreproducibility).
- Fix shipped if applicable.
- Forensic trail wired.

## Blocking

- None.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<root cause + fix sha or unreproducible-with-trace doc sha>"`
