# TASK-E2E-004: Multi-agent regression runner — Claude sub-agents fanning out on failures

**Priority**: P2 (layer-2 on top of working suite; doesn't gate the suite itself)
**Status**: Proposed
**Depends on**: TASK-E2E-001 / 002 / 003 shipped and green

---

## Problem

When a flow test fails in CI, a human investigates: reads the Playwright trace, hypothesizes the regression, grep-finds the recent change, pushes a fix. That's 30-60 min of operator toil per failure.

A multi-agent runner delegates that toil. Each failing spec spawns a scoped Claude sub-agent in its own git worktree + tenant:

- Reads the Playwright trace artifact (video + HAR + console log)
- Runs `git log -p --since="2 days ago"` on the surfaces the spec touched
- Proposes a hypothesis → patch → re-runs the spec → reports back
- Coordinator (the main Claude session, or CI post-step) reviews the patch; if green, merges

## Architecture

```
CI green-path:
  Playwright all green → done.

CI red-path:
  ├─ For each failing spec:
  │    ├─ coordinator.enqueueTriage(spec, trace-artifacts)
  │    ├─ sub-agent claims; worktree .claude/worktrees/triage-<spec-id>
  │    ├─ sub-agent inspects trace + git history
  │    ├─ sub-agent proposes patch + re-runs ONLY this spec with own tenant
  │    └─ sub-agent reports: FIXED | CANT-FIX <reason>
  └─ coordinator reviews FIXED results in parallel, merges confident ones
```

## Isolation guarantees between sub-agents

- **Tenant**: each sub-agent uses `tenant_id = t_triage_<worker-id>_<timestamp>`. No cross-talk.
- **Worktree**: each sub-agent in `.claude/worktrees/triage-<spec-id>/` (git-ignored; per CLAUDE.md convention). Patches land on branch `claude-subagent-triage-<spec>/<task-id>`.
- **Playwright workers**: each sub-agent invokes its own Playwright process with `--workers=1 --grep <spec-title>` — no shared state.
- **Docker stack**: shared. The stack isolation above is what makes this safe; adding per-sub-agent compose projects costs 2 min of boot each and we don't need it.

## Failure modes this prevents

- Sub-agent A "fixes" a spec by weakening the assertion → sub-agent B running the adjacent spec catches the regression before merge.
- Two sub-agents editing the same file — worktree isolation guarantees no collision; coordinator merges sequentially.
- Flake masquerading as regression — coordinator requires sub-agent to prove the spec is red on `origin/main` before accepting the fix branch.

## Scope for first cut

- `scripts/e2e-flow/triage-spec.sh <spec-title>` — wrapper that creates the worktree + spawns the sub-agent
- CI hook: on e2e-flow red, the workflow posts a comment listing `gh workflow run triage-e2e --field spec=<name>` for each failure
- No auto-merge — coordinator always reviews

## Non-goals (v1)

- Auto-merging sub-agent patches (too risky for flow tests; keep human in loop)
- Cross-spec hypothesis ("the same regression broke all three subscription tests") — human job for now
- Sub-agent fix budgets / cost accounting — add when we see abuse

## Done when

- Runbook in this doc is executable by a coordinator without tribal knowledge
- `scripts/e2e-flow/triage-spec.sh` exists + tested
- First triage run on a planted regression lands a correct fix within 10 min
