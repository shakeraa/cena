# Git workflow — coordinator and worker rules

Status: durable. Read before any direct push to `main` or merge to `main`.

## Why this exists

On 2026-05-03 we hit a sync-race incident:

1. A feature branch was merged into `main` via the GitHub UI.
2. A local checkout on `main` ran `git pull --ff-only origin main` shortly after.
3. `git pull` reported "Already up to date" — but in fact `origin/main` had advanced via the merge moments earlier and the local cache had not yet refreshed.
4. A subsequent `git push origin main` (carrying older local commits as the "new" tip) silently overwrote the merge tree on the remote.
5. The merge was recovered from the reflog, but only because someone noticed a missing file. In a worse alignment of timing this would have been a quiet data-loss event in `main`.

The workflow allowed it. This doc closes it.

## Hard rules (apply to every push to `main`)

These rules are non-negotiable. The coordinator should reject any commit-history that proves a rule was skipped (e.g. a force-push to `main` lacking `--force-with-lease`).

### Rule 1 — `fetch` before any compare to `origin/main`

`git pull` (or `git pull --ff-only`) does NOT guarantee the local cache of `origin/main` matches the remote at the moment of the call. Network jitter, GitHub PR-merge race, or a stale background fetch can leave the local view behind by minutes.

**Always:**

```bash
git fetch origin main
```

before reading `origin/main` in any flow that decides what to push. `fetch` is the one operation that hits the remote synchronously and refreshes the local ref.

### Rule 2 — Rebase onto `origin/main` immediately before push

Even after `fetch`, if you've been working locally for any length of time, your branch (or your local `main`) may have diverged from a recently merged PR. Rebase the moment before you push:

```bash
git fetch origin main
git rebase origin/main      # NOT 'git pull --rebase' — see Rule 1
```

If the rebase surfaces conflicts, resolve them. Do NOT skip with `git rebase --skip` unless you understand exactly which commits you're discarding. Do NOT use `git rebase -i` here — interactive rebase is for history rewriting on feature branches before review, not for the final push-to-main step.

`git rebase --no-edit` is NOT a valid option. If you need an automated path, use `git rebase --autostash` to handle dirty trees.

### Rule 3 — Push to `main` with `--force-with-lease=origin/main`

Plain `git push origin main` after a rebase will fail (non-fast-forward) — that's by design. If you genuinely need to publish a rebased local `main`, use `--force-with-lease=origin/main`:

```bash
git push --force-with-lease=origin/main origin main
```

`--force-with-lease=origin/main` says: "force-push, but only if my view of `origin/main` matches the remote's view at this exact moment." If someone else pushed between your last `fetch` and this `push`, the lease check fails and the push is rejected — which is exactly the behavior the 2026-05-03 incident lacked.

NEVER use plain `--force` on `main`. Plain `--force` does not check the remote-tip against your local view, and is the operation that causes silent overwrites.

### Rule 4 — Coordinator-gate for direct merges to `main`

Per [CLAUDE.md](../../CLAUDE.md) and [AGENTS.md](../../AGENTS.md), no worker pushes to `main` directly. Workers push to feature branches named `<worker>/<task-id>-<slug>`; the coordinator reviews and merges via PR (or local merge with the rules above).

Even the coordinator should pause and confirm before:

- `git merge` into `main` (whether via CLI or PR-merge UI)
- `git push origin main` (force or otherwise)
- A PR merge that targets `main` directly

The coordinator should ask the user before merging unless the user has explicitly green-lit the merge in the same session. See `~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_check_with_coordinator_before_merge.md` for the durable rule.

## Worker workflow (feature branches)

Workers operate on feature branches inside their own worktrees. The flow is:

```bash
# Setup (once per task)
git worktree add .claude/worktrees/<task-id> -b <worker>/<task-id>-<slug> origin/main
cd .claude/worktrees/<task-id>

# Work + commit (small atomic units)
git add -A && git commit -m "fix: ..." && git push -u origin <worker>/<task-id>-<slug>

# Before the final push, sync with origin/main (NOT main local — see Rule 1)
git fetch origin main
git rebase origin/main      # resolve conflicts inline if any

# Push (force-with-lease because rebase rewrites SHAs)
git push --force-with-lease=origin/<worker>/<task-id>-<slug>

# Hand off to coordinator
node .agentdb/kimi-queue.js complete <task-id> --worker <worker> --result "branch=<worker>/<task-id>-<slug>, ..."
```

**Workers must never:**

- Push to `main` (Rule 4).
- Force-push without `--force-with-lease` (Rule 3).
- Skip the `fetch` → `rebase` → `push --force-with-lease` chain when their branch has been open more than ~10 minutes (Rules 1 + 2).

## Coordinator workflow (merging branches)

```bash
# Pre-merge: refresh view of origin
git fetch origin main

# Inspect the branch under review
git log --oneline origin/<worker>/<task-id>-<slug> ^origin/main
git diff origin/main..origin/<worker>/<task-id>-<slug>

# Either merge via GitHub UI (and re-fetch after) OR locally:
git checkout main
git pull --ff-only origin main           # advisory, may be stale by milliseconds
git fetch origin main                    # authoritative — Rule 1
git merge --ff-only origin/<branch>      # or --no-ff for an explicit merge commit
git push --force-with-lease=origin/main origin main

# Post-merge cleanup
git push origin --delete <worker>/<task-id>-<slug>     # optional, after confirming merge landed
git worktree remove .claude/worktrees/<task-id>
```

## Recovery — if a sync-race happens anyway

If the merge tree has been silently overwritten (the 2026-05-03 mode):

1. **Stop pushing.** Every additional push compounds the divergence.
2. **Find the lost commit in the reflog.**
   ```bash
   git fetch origin main
   git reflog
   ```
   The merge commit will appear as the previous tip of `origin/main` in the reflog if you fetched it before the overwrite.
3. **If the reflog has it locally**, hard-reset `main` to that SHA + force-push-with-lease.
4. **If the reflog doesn't have it**, check GitHub's branch history under "Insights → Network" — the GitHub-side reflog still has it for ~30 days even after a forced overwrite.
5. **Document the incident** in this doc's history section (below) and re-read Rule 3 before the next push.

## History

- **2026-05-03**: this doc landed in response to a near-data-loss sync-race incident. Recovered from reflog. Closed the workflow gap with the four rules above.
