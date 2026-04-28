# TASK-PRR-263: Audit + clean up 3 unmerged claude-code/* branches

**Priority**: P2 — housekeeping; potential lost work
**Effort**: XS (30 min; investigation + decision per branch)
**Source docs**: claude-code self-audit 2026-04-28
**Assignee hint**: claude-code (coordinator) or any Claude session with git access
**Tags**: source=claude-code-audit-2026-04-28,priority=p2,git-housekeeping
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: (no epic; standalone hygiene)

---

## Goal

`git branch --no-merged main | grep claude-code/` returns 3 branches predating the 2026-04-28 session:
- `claude-code/signalr-redis-auth-fix-2026-04-24`
- `claude-code/t_8a3cb8f34875-rdy081-phase01-insurance`
- `claude-code/t_9c67e81050d6-k8s-local-m1`

Coordinator (this session) dismissed them as "predate this session" without verifying ownership / dates / whether they contain unfinished work that should land. This task closes the gap.

## Scope

1. For each branch:
   - `git log origin/main..origin/<branch>` — what commits are unmerged?
   - `git show <branch-tip-sha>` — what's the actual delta?
   - Trace branch ownership: queue task ID (where applicable), original session author, date created.
   - Decide: merge to main, abandon (delete), or hand off (re-assign with explicit owner).
2. Document findings in `tasks/pre-release-review/reviews/PRR-263-stale-branch-audit.md`.
3. Execute decisions: merge, delete, or reassign each branch.
4. Add a CI / cron note (or manual procedure) to flag claude-code/* branches with no activity in 14 days for re-audit.

## Definition of Done

- All 3 branches resolved (merged / deleted / reassigned with explicit owner).
- Audit findings file filed.
- Stale-branch detection wired or documented.

## Blocking

- None.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch decisions per the 3 branches + audit findings sha>"`
