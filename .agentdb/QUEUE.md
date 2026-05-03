# Cena Multi-Agent Task Queue — Coordination Protocol

**Database**: [.agentdb/kimi-queue.db](kimi-queue.db) (SQLite, WAL mode, safe for concurrent access)
**CLI**: [.agentdb/kimi-queue.js](kimi-queue.js)
**Purpose**: Coordinate work across multiple coding agents — Claude Code, Kimi Code, sub-agents, human operators — with a single authoritative source of truth for *what needs to be done*, *who is doing it*, and *what's already finished*.

---

## Why a shared queue

Cena has multiple agent surfaces that can write code:

- **Claude Code** (this session) — planning, reviewing, architecture, critical work
- **Claude sub-agents** — spawned via the Task tool for parallel execution
- **Kimi Code** — separate process, cheaper runs, independent code paths
- **Human operators** — driving any of the above manually

Without a queue they step on each other. With a queue, every actor pulls from the same FIFO-by-priority list, claims atomically, and writes results back to the same place. Review and "push to main" stays in one coordinator's hands (Claude Code by default).

---

## Task states

```
pending ──claim──▶ in_progress ──complete──▶ done
                        │
                        ├─fail──▶ failed   (needs human triage)
                        │
                        └─release──▶ pending  (worker gave up cleanly)
```

- **pending**: in the queue, claimable by any matching worker
- **in_progress**: claimed and currently being worked on by exactly one worker
- **done**: completed, result text stored in the row
- **failed**: worker hit a blocker and declared failure with a reason

Only `pending` tasks can be `update`d or `delete`d. `in_progress` tasks are held by a specific worker and only that worker (or the human coordinator via release) can move them. `done` and `failed` tasks are immutable history.

---

## Atomicity

The claim operation is a single SQL statement:

```sql
UPDATE tasks
SET status='in_progress', worker=?, claimed_at=?, updated_at=?
WHERE id=? AND status='pending'
```

SQLite serializes writers with its WAL lock, so two workers calling `claim` on the same task at the same time will see exactly one `changes=1` and one `changes=0`. The loser gets a clear conflict error and exits with code 3.

No worker can "accidentally" steal a task that is already in progress or done.

---

## Handshake (first-join ceremony)

Before a new worker can pull real tasks, the coordinator issues a one-shot **handshake task** that the worker must claim and complete cleanly. The handshake proves:

- The worker's environment can reach the DB (`better-sqlite3` loads)
- The worker can use `claim` / `show` / `complete` correctly
- The worker has read [AGENT_CODER_INSTRUCTIONS.md](AGENT_CODER_INSTRUCTIONS.md), [QUEUE.md](QUEUE.md), and [CLAUDE.md](../CLAUDE.md)
- The worker can echo a **rotating phrase** unique to that specific handshake body (prevents replay-from-memory)

### Coordinator enqueues a handshake

```bash
node .agentdb/kimi-queue.js handshake <worker-name>
```

Prints the new task ID and a per-handshake rotating phrase. The phrase is also embedded inside the task body as `PHRASE: <value>`. The coordinator records the task ID for later verification.

### Worker completes the handshake

1. `list --assignee <worker-name>` → find the handshake
2. `claim <task-id> --worker <worker-name>`
3. `show <task-id>` → read the body, extract the PHRASE
4. Verify environment: `node -e "require('better-sqlite3'); console.log('ok')"`
5. `complete <task-id> --worker <worker-name> --result "phrase=<value>,worker=<name>,node=<version>,os=<Darwin|Linux>,files-read=3/3,ready=true"`

### Coordinator verifies

```bash
node .agentdb/kimi-queue.js verify <task-id>
```

Checks that the result contains the exact phrase from the enqueue event and all six required fields. Exit code 0 = PASS, 3 = FAIL. On FAIL, the coordinator re-enqueues a fresh handshake; the failed one stays in history.

### Enforcement

The handshake is **advisory, not blocking**. The CLI does not refuse real task claims from unshook workers. Enforcement is social: the coordinator only enqueues or greenlights real work for workers with a passing handshake on record.

### Why rotating phrases

Each handshake phrase is an 8-byte random hex token generated at enqueue time. It lives only inside the target task's body. A worker that cannot produce the exact phrase either did not read its own task body (copy-pasted from a memory of a previous handshake) or did not claim the right task. Either way, the verification fails loudly.

---

## Worker names

A worker is a free-form string; use these conventions in this repo:

| Worker name | Who |
|---|---|
| `claude-code` | The main Claude Code session (usually the planner / reviewer) |
| `claude-subagent-<purpose>` | A Claude Code sub-agent spawned via the Task tool (e.g. `claude-subagent-db00`) |
| `kimi-code` | A generic Kimi Code CLI session |
| `kimi-coder` | Kimi Code acting in a "coder" role |
| `kimi-reviewer` | Kimi Code acting as a second-pair reviewer |
| `human` | A human running the task manually |

Workers are advisory for logging. The queue enforces claims by whatever string the worker passes — pick a name and stick with it for a session.

---

## Assignees

`assignee` is a *soft preference*. When you enqueue, you can set an assignee like `kimi-coder`; `next --assignee kimi-coder` will prefer unclaimed tasks whose assignee is kimi-coder OR unassigned, in that order. Nothing stops another worker from claiming it — the queue is work-stealing by design. Set assignee only when routing matters.

---

## The golden loop (for any worker)

```bash
# 1. Peek at the next task without claiming it
node .agentdb/kimi-queue.js next --assignee <worker> --json

# 2. Claim it atomically
node .agentdb/kimi-queue.js claim <id> --worker <worker>

# 3. Read the full task including body
node .agentdb/kimi-queue.js show <id>

# 4. Do the work. Read the files the body references. Run tests. Commit if instructed.

# 5. Complete or fail
node .agentdb/kimi-queue.js complete <id> --worker <worker> --result "summary + PR link"
# OR
node .agentdb/kimi-queue.js fail <id> --worker <worker> --reason "why"
```

If something goes wrong before you've committed anything and another worker would be better placed:

```bash
node .agentdb/kimi-queue.js release <id> --worker <worker>
```

---

## Rules every coder-agent MUST follow

1. **Read the body fully before starting.** Every task body contains: goal, files to read, files to modify, constraints, definition of done. Skimming wastes everyone's time.

2. **Always pass `--worker`.** The queue does not trust you if you don't identify yourself. Use a consistent name across the whole session.

3. **Never work on a task you haven't claimed.** If you skip the claim step you can't complete it, and a parallel worker may be halfway through it already.

4. **Only the claimant can complete or fail.** The queue enforces this. If you see a task is held by someone else, leave it alone.

5. **Fail loudly, don't silently drop.** If you can't finish, call `fail` with a real reason. A failed task surfaces in `stats` and gets triaged by the coordinator.

6. **Results are plain text, not diffs.** The `--result` field should be a 3-10 line human-readable summary with:
   - What changed (high level)
   - Which files were touched
   - Test/lint/build status
   - A PR or commit link if applicable

7. **Commit your work under your worker identity.** If you are `kimi-coder`, your commits should be identifiable (at minimum in the commit message trailer). Do not commit as "Claude Code" if you are Kimi. This is traceability, not vanity.

8. **Never push to `main` directly.** The coordinator (default: `claude-code`) reviews completed tasks and handles the push. Open a branch, commit, push the branch, link the branch or PR in the result.

9. **If the task requires spawning sub-agents, claim and use sub-agents, do not re-enqueue the same task.** Re-enqueueing doubles the queue noise. The queue tracks single-owner work; your sub-agents are your problem.

10. **Touch a task once per state transition.** Don't call `claim` twice, don't call `complete` on a done task, don't edit a task body while you own it — edit the body before claiming (as the coordinator) or complete with a result that describes the refinement.

11. **Respect the priority order.** `critical > high > normal > low`. Within the same priority, oldest first. Don't cherry-pick easy tasks out of order.

12. **When in doubt, ask the coordinator before acting.** Especially for `delete`, `update`, or any task marked with the tag `needs-review`.

---

## Coordinator responsibilities (default: claude-code)

The coordinator is the single role responsible for:

- **Enqueueing** tasks (with clear, self-contained bodies)
- **Reviewing** completed tasks — reading the result, spot-checking the diff, merging to main
- **Triaging failed tasks** — reading the reason, deciding whether to re-enqueue, split, or abandon
- **Deleting** stale tasks that are no longer relevant
- **Updating** task bodies when the spec changes
- **Pushing** branches to `main` after review

Non-coordinator workers (kimi-coder, claude-subagent-*, etc.) SHOULD NOT perform these actions unless the coordinator explicitly delegates.

---

## Enqueue conventions

When writing a task body, follow this structure so any worker can pick it up cold:

```markdown
## Goal
<1-3 sentences>

## Context
<why this task exists, what upstream decision drove it, links to relevant docs>

## Files to read first
- path/to/file.cs
- path/to/other.md#L30-L50

## Files to modify / create
- path/to/file.cs (modify function X)
- path/to/new-file.ts (create)

## Constraints
- Must pass existing tests
- Must not break <constraint>
- Style: <lint rules, DDD bounded contexts, etc>

## Definition of done
- [ ] concrete, checkable outcome 1
- [ ] concrete, checkable outcome 2
- [ ] concrete, checkable outcome 3

## Reporting
When you complete this task, the `--result` field should include:
- 1-sentence summary
- Modified files list
- Test run output (pass/fail counts)
- Branch name + link, if you pushed a branch
```

Use `--body-file` to pass the markdown from a file rather than inlining a giant string on the command line.

---

## Tags

Tags are a comma-separated string in the `tags` column. Use them for routing and filtering:

- `db`, `infra`, `frontend`, `backend`, `docs`, `tests`
- `safe` — can run without human review (rare)
- `needs-review` — coordinator must see the result before merge
- `blocked` — cannot proceed until a dependency clears
- `review-only` — audit/review task, no code changes expected

Filter with `list --status pending` and visually scan tags, or grep the JSON output.

---

## Examples

### Coordinator enqueues a task

```bash
cat > /tmp/task-db00.md <<'EOF'
## Goal
Fix the pgvector dimension drift in src/infra/docker/init-db.sql.

## Context
init-db.sql declares vector(384) but the real deployed schema is vector(1536).
See docs/tasks/infra-db-migration/TASK-DB-00-pgvector-dimension-drift.md for full detail.

## Files to read first
- docs/tasks/infra-db-migration/TASK-DB-00-pgvector-dimension-drift.md
- src/infra/docker/init-db.sql
- scripts/sql/001_pgvector_embeddings.sql
- src/actors/Cena.Actors/Services/PgVectorMigrationService.cs

## Files to modify
- src/infra/docker/init-db.sql

## Definition of done
- [ ] init-db.sql uses vector(1536)
- [ ] Index type is hnsw, not ivfflat
- [ ] grep for '384' returns no DB-related matches
- [ ] Fresh docker compose up produces a table matching PgVectorMigrationService's shape
EOF

node .agentdb/kimi-queue.js enqueue "DB-00: Fix pgvector dimension drift" \
  --body-file /tmp/task-db00.md \
  --priority critical \
  --tags db,infra,safe \
  --assignee kimi-coder
```

### Worker picks up and completes

```bash
# Peek
node .agentdb/kimi-queue.js next --assignee kimi-coder

# Claim
node .agentdb/kimi-queue.js claim t_abc123 --worker kimi-coder

# Do the work
# ... edit init-db.sql, run docker compose up, verify ...

# Complete
node .agentdb/kimi-queue.js complete t_abc123 --worker kimi-coder \
  --result "Changed init-db.sql from vector(384)/ivfflat to vector(1536)/hnsw. Files: src/infra/docker/init-db.sql. No other references to 384 found. Branch: fix/db-00-pgvector-dim, pushed."
```

### Coordinator reviews completed work

```bash
# See everything done today
node .agentdb/kimi-queue.js list --status done

# Read a specific result
node .agentdb/kimi-queue.js show t_abc123

# Check the git branch the worker pushed, run tests, merge, done.
```

---

## Stats

```bash
node .agentdb/kimi-queue.js stats
```

Expected output:

```
by status:
  pending      3
  in_progress  1
  done         7
  failed       0
by assignee:
  (unassigned)  2
  kimi-coder    6
  claude-code   3
by worker:
  claude-code     2
  kimi-coder      5
```

Use this as a daily standup: what's done, what's in flight, what's stuck.

---

## Failure modes and recovery

### "conflict: task X is in_progress (held by Y)"
You tried to claim / complete / fail / release a task someone else owns. Pick another task with `next`.

### "conflict: task X is pending, cannot complete"
You forgot to claim first. Claim, then complete.

### "not found: X"
You typo'd the ID. Run `list` to see real IDs.

### A task is stuck in `in_progress` for hours with no updates
The worker crashed or lost context. The coordinator runs:

```bash
node .agentdb/kimi-queue.js show <id>
# check last event timestamp
# if truly stale, force-release by editing the DB (last resort) or fail it:
```

There is no force-release CLI command on purpose — the friction is the point. The coordinator must consciously decide the previous worker is gone.

### A failed task needs to be retried
The coordinator re-enqueues it with a fresh body that includes context on why the previous attempt failed:

```bash
node .agentdb/kimi-queue.js show <failed-id>    # read the failure reason
node .agentdb/kimi-queue.js enqueue "DB-00 retry: Fix pgvector drift (v2)" --body-file ... --priority critical
```

The failed task stays in history as a record.

---

## What this queue is NOT

- **Not a scheduler.** It has no cron, no delayed-execution, no retries. A worker polls `next` when it's free.
- **Not a memory store.** For shared *memories / patterns / learning*, use the AgentDB ReasoningBank (separate database, separate concern).
- **Not a pub/sub.** Workers must poll. If you want push notifications, build a watcher script on top.
- **Not a distributed consensus system.** It works because SQLite WAL is fast and local. Cross-machine workers over a network share would need a different design.

---

## Messaging (agent-to-agent communication)

The queue also carries **messages** between workers — not just tasks. Use messages when you need to send a free-form note, status update, question, directive, or broadcast that doesn't fit the task body → result pattern.

### Message kinds

| Kind | When to use |
|---|---|
| `note` | Informational; no response expected |
| `status` | A worker reports progress on its current activity |
| `question` | Worker asks the coordinator (or another worker) something; expects an answer |
| `answer` | Reply to a question, linked by `--correlation` |
| `directive` | Coordinator tells a worker to do something (usually on a broadcast topic) |
| `ack` | Explicit acknowledgement — rare, usually recv auto-acks |

### Send

```bash
# Direct message
node .agentdb/kimi-queue.js send --from claude-code --to kimi-coder \
  --kind note --subject "reminder" --body "Don't forget the Co-Authored-By trailer"

# Linked to a task (adds a task_id reference so the recipient knows the context)
node .agentdb/kimi-queue.js send --from kimi-coder --to claude-code \
  --kind status --subject "DB-00 in progress" \
  --body "Edits done, running docker compose up now" --task t_95a77a446c72

# Question with correlation for easy answer tracking
node .agentdb/kimi-queue.js send --from kimi-coder --to claude-code \
  --kind question --subject "scope check" \
  --body "Do you want init-db.sql to drop-and-recreate or alter?" --correlation q42

# Answer — same correlation ID
node .agentdb/kimi-queue.js send --from claude-code --to kimi-coder \
  --kind answer --subject "re: scope check" \
  --body "Drop and recreate. Fresh containers only." --correlation q42

# Broadcast to a topic (anyone who recvs on that topic will see it)
node .agentdb/kimi-queue.js send --from claude-code --topic coordination \
  --kind directive --subject "priority shift" \
  --body "DB-00 becomes critical; park everything else"
```

### Receive

```bash
# Pull all direct messages to you (auto-acks on default)
node .agentdb/kimi-queue.js recv --worker kimi-coder

# Also pull messages on a topic
node .agentdb/kimi-queue.js recv --worker kimi-coder --topic coordination

# Peek without consuming (useful to see what's waiting without clearing it)
node .agentdb/kimi-queue.js recv --worker kimi-coder --peek

# Filter by kind
node .agentdb/kimi-queue.js recv --worker claude-code --kind question

# Only messages since a timestamp (ms since epoch)
node .agentdb/kimi-queue.js recv --worker kimi-coder --since 1775808000000
```

`recv` is **pull-based**. There is no push or notification. The loop you run is:

```bash
while true; do
  node .agentdb/kimi-queue.js recv --worker <name>
  sleep 30  # or whatever interval works
done
```

Or simpler — check when you're about to do something important ("before I claim the next task, are there any directive messages waiting?").

### Ack

Normally `recv` auto-acks. Use explicit `ack` only when you peeked and now want to clear it:

```bash
node .agentdb/kimi-queue.js recv --worker kimi-coder --peek  # look
node .agentdb/kimi-queue.js ack m_abc123 --worker kimi-coder  # clear one
```

### Worker registry and heartbeats

Every send, recv, and heartbeat touches the `workers` table, updating `last_seen`. Any agent can list who else is around:

```bash
# All workers ever seen
node .agentdb/kimi-queue.js workers

# Only those seen in the last 5 minutes
node .agentdb/kimi-queue.js workers --active

# Register a liveness check without sending anything
node .agentdb/kimi-queue.js heartbeat --worker kimi-coder --status active
```

Use heartbeats when you want to declare presence without any activity. Typical frequency: once per minute while idle, or before/after every task claim.

### Message retention

Consumed messages stay in the DB forever (for audit). If the messages table gets big, the coordinator can prune with a direct SQL delete — no CLI command for this on purpose (low-risk on the current scale).

### Messaging rules

1. **Always pass `--from`.** Messages are always signed by the sender.
2. **Never use messages to bypass the task queue.** If you want someone to do work, enqueue a task. Messages are for coordination, not work assignment.
3. **Do not spam.** A status per task is fine; a status per minute is noise.
4. **Questions should have correlation IDs.** Even a short one like `q1`, `q2`, etc. makes answers trackable.
5. **Directives on the `coordination` topic are authoritative.** If the coordinator broadcasts a directive, every active worker should recv and honor it before claiming more work.

### Topics in use

| Topic | Purpose |
|---|---|
| `coordination` | Coordinator directives — priority shifts, stops, refocuses |
| `status` | Free-form status updates from workers |
| `learnings` | Insights any worker wants to share (e.g. "watch out for this gotcha") |

New topics can be created implicitly — just `send --topic <new-name>` and recv-ers will see it.

---

## Related docs

- [CLAUDE.md](../CLAUDE.md) — project-wide agent rules; references this queue in the "Multi-Agent Coordination" section
- [.claude/agents/core/coder.md](../.claude/agents/core/coder.md) — the core coder agent definition with queue-aware hooks
- [docs/tasks/infra-db-migration/README.md](../docs/tasks/infra-db-migration/README.md) — the source of many first tasks enqueued
- [tasks/student-web/README.md](../tasks/student-web/README.md) — student web task bundle
- [tasks/student-backend/README.md](../tasks/student-backend/README.md) — student backend task bundle
