# Instructions for Coder Agents Working on Cena

**Audience**: Any coding agent working on the Cena codebase — Claude Code, Kimi Code, Claude sub-agents, or human operators driving any of the above.

**Read order before starting ANY task**:

**If you are Claude Code** (main session or sub-agent):
1. [../CLAUDE.md](../CLAUDE.md) — project rules, architecture, file organization, security
2. This file — multi-agent coordination protocol
3. [.agentdb/QUEUE.md](QUEUE.md) — task queue CLI and state machine
4. The task body itself (`node .agentdb/kimi-queue.js show <id>`)
5. The feature spec(s) referenced in the task body

**If you are ANY OTHER agent** (Kimi Code, Codex CLI, Cursor, Aider, human, etc.):
1. [../AGENTS.md](../AGENTS.md) — **self-contained** project rules + onboarding (do NOT read CLAUDE.md)
2. This file — multi-agent coordination protocol
3. [.agentdb/QUEUE.md](QUEUE.md) — task queue CLI and state machine
4. The task body itself (`node .agentdb/kimi-queue.js show <id>`)
5. The feature spec(s) referenced in the task body

If you skipped any of the above, stop and go back.

---

## First join: HANDSHAKE required

**Before you claim any real task**, you must complete your handshake task.

A handshake is a zero-side-effect task the coordinator enqueues for you individually when you first join the bus. It proves:

1. You can read the protocol docs (required reading listed above)
2. You can use the CLI (`claim`, `show`, `complete`)
3. You own the identity you say you own (by echoing a rotating phrase unique to YOUR handshake body)
4. Your environment is healthy (Node version, better-sqlite3 installed, OS reachable)

### How to do your handshake

1. Ask the coordinator to enqueue a handshake for your worker name:

   ```text
   Coordinator, please enqueue a handshake for worker "<your-worker-name>".
   ```

   The coordinator runs:
   ```bash
   node .agentdb/kimi-queue.js handshake <your-worker-name>
   ```

   This prints the new task ID and a per-handshake rotating phrase. The phrase is also embedded in the task body as `PHRASE: <value>`.

2. **Find your handshake task** — it will be the only `pending` task with tag `handshake` assigned to your worker name:

   ```bash
   node .agentdb/kimi-queue.js list --assignee <your-worker-name>
   ```

3. **Read the body fully.** It walks you through the entire flow.

4. **Claim it:**

   ```bash
   node .agentdb/kimi-queue.js claim <task-id> --worker <your-worker-name>
   ```

5. **Complete it** with a result string containing ALL of:
   - `phrase=<the exact PHRASE value from YOUR task body>`
   - `worker=<your-worker-name>`
   - `node=<output of node --version>`
   - `os=<output of uname -s>`
   - `files-read=3/3`
   - `ready=true`

   Example:
   ```bash
   node .agentdb/kimi-queue.js complete <task-id> --worker <your-worker-name> \
     --result "phrase=abc123,worker=<your-worker-name>,node=v20.11.0,os=Darwin,files-read=3/3,ready=true"
   ```

6. **Wait for the coordinator's green light.** The coordinator verifies via:

   ```bash
   node .agentdb/kimi-queue.js verify <task-id>
   ```

   On PASS, you are cleared to pull real work. On FAIL, the coordinator re-enqueues a fresh handshake and you start over.

### Rules during handshake

- You may not `claim` any other task before your handshake transitions to `done` AND the coordinator's `verify` returns PASS.
- Do not modify any files during handshake — it is a read-only probe.
- Do not guess the phrase. It is unique to YOUR task body. If you cannot find it, call `fail` with `--reason "phrase not found in body"` and the coordinator will investigate.
- If `better-sqlite3` fails to load, your handshake fails with a clear environment error, not silently.

**The handshake is advisory, not enforced by the CLI.** It exists so humans and the coordinator can gate real work behind a proof-of-readiness. If you skip it and start claiming real tasks, you break trust in the coordination protocol and the coordinator will revoke your identity from the task queue.

---

## Who you are (and why it matters)

You are one of several coder-agents working in parallel on the same repository. You are **not** the only coder, and you are **not** the coordinator. Your job is to:

1. Pull a task from the shared queue
2. Claim it under your worker identity
3. Execute the task exactly as written
4. Report results via `complete` or `fail`
5. Go back to step 1

The coordinator (by default the Claude Code main session, worker name `claude-code`) owns the global plan, reviews your output, and merges to `main`. You do not bypass the coordinator.

---

## Your worker identity

Every CLI call you make against the queue needs `--worker <your-name>`. Pick one name for your whole session and use it consistently:

| You are | Use worker name |
|---|---|
| Kimi Code CLI session | `kimi-code` or `kimi-<role>` (kimi-coder, kimi-reviewer, etc.) |
| Claude Code Task-tool sub-agent | `claude-subagent-<purpose>` |
| Human running tasks manually | `human-<yourname>` |
| Claude Code main session | `claude-code` (this is the coordinator — only use if you are the coordinator) |

---

## The loop you run

Every time you are free to do work:

```bash
# 1. Check the queue for the next task matching your identity
node .agentdb/kimi-queue.js next --assignee <worker>

# 2. If a task is returned, claim it
node .agentdb/kimi-queue.js claim <id> --worker <worker>

# 3. Read the full task
node .agentdb/kimi-queue.js show <id>

# 4. Execute. See "Execution rules" below.

# 5. Report
node .agentdb/kimi-queue.js complete <id> --worker <worker> --result "..."
# OR
node .agentdb/kimi-queue.js fail <id> --worker <worker> --reason "..."

# 6. Loop
```

If `next` returns `(queue empty)`, stop and wait. Do not invent work.

---

## Execution rules

### Before you touch any file

- [ ] Read every file listed under "Files to read first" in the task body
- [ ] Read any CLAUDE.md in the directory tree relevant to the task
- [ ] Check for existing tests covering the area you are about to change
- [ ] Confirm you understand the Definition of Done

If the task body is ambiguous, `fail` with a reason asking for clarification. Do not guess at intent.

### While working

- **Never** save working files, markdown, or tests to the repo root. Use `/src`, `/tests`, `/docs`, `/config`, `/scripts`, `/examples`.
- **Never** create new files unless the task explicitly requires it. Prefer editing existing files.
- **Never** create documentation (`*.md`) files proactively. Only if the task says so.
- **Never** commit secrets, `.env` files, or credentials. CLAUDE.md has the full ignore list.
- **Always** read a file before editing it.
- **Always** follow DDD with bounded contexts. Keep files under 500 lines.
- **Always** use typed interfaces for public APIs.
- **Always** validate input at system boundaries.
- If the task touches event-sourced state, use event upcasters for schema evolution (see [src/shared/Cena.Infrastructure/EventStore/EventUpcasters.cs](../src/shared/Cena.Infrastructure/EventStore/EventUpcasters.cs) for the pattern).

### Worktree isolation (REQUIRED for every non-trivial task)

All non-trivial work happens in a dedicated git worktree so multiple workers (claude-code, kimi-coder, sub-agents) do not collide in the shared working directory. The worktrees share `.git/` but have separate on-disk directories.

Per-worker worktree roots (both git-ignored):

- Claude sub-agents → `.claude/worktrees/<task-id>/`
- Kimi and other external workers → `.agentdb/worktrees/<task-id>/`
- Claude Code main session → main repo root (it's the coordinator, not a worker-per-task)

Flow after `claim`:

```bash
cd /Users/shaker/edu-apps/cena     # main repo root
git fetch origin
git worktree add <worktree-path>/<task-id> -b <worker>/<task-id>-<slug> origin/main
cd <worktree-path>/<task-id>
# ... do all work here ...
git add -A && git commit -m "..." && git push -u origin <branch>
cd /Users/shaker/edu-apps/cena
node .agentdb/kimi-queue.js complete <task-id> --worker <w> --result "..."
```

Rules:

- One worktree per task. Fresh from `origin/main`. Never reuse.
- Never edit files in the main worktree during a claimed task.
- Run your full build + test suite **inside** the worktree.
- Never prune or remove another worker's worktree.
- `git worktree list` shows all active worktrees; you should see yours + the main. Don't touch others.

For trivial single-line edits, the task body may explicitly waive the worktree requirement. Otherwise worktrees are mandatory.

### Branch, commit, push — never merge

1. Create a feature branch named `<worker>/<task-id>-<slug>`, e.g. `kimi-coder/t_abc123-fix-pgvector-drift`
2. Commit in small atomic units
3. Identify yourself in commit trailers:

   ```text
   Fix pgvector dimension drift in docker init

   Task: t_abc123
   Co-Authored-By: kimi-coder <kimi@cena.local>
   ```

4. Push your branch to the remote (`git push -u origin <branch>`)
5. **Do NOT open a PR, do NOT merge to main.** The coordinator handles that step. Your `complete --result` should include the branch name and what the coordinator needs to review.

### If you need to run commands

- `npm test`, `dotnet test`, `pytest` — yes, always before completing
- `npm run lint`, `dotnet format` — yes, before completing
- `docker compose up` — yes, if the task calls for it, but shut it down after
- `git push --force` — **never** without explicit authorization from the task body
- `git reset --hard`, `rm -rf`, `git checkout .` — **never** without explicit authorization
- Skipping hooks (`--no-verify`) — **never**

### If you hit a blocker

1. Do not commit half-finished work to a shared branch
2. Call `fail <id> --worker <you> --reason "<specific, actionable reason>"`
3. If you have partial work worth saving, push it to a throwaway branch named `wip/<worker>/<task-id>` and mention the branch in the failure reason
4. Move on

---

## Reporting results

A good `--result` string looks like:

```text
Changed src/infra/docker/init-db.sql from vector(384)/ivfflat to vector(1536)/hnsw.
Files touched:
  - src/infra/docker/init-db.sql (embedding table + index)
Tests:
  - docker compose up succeeds, table shape matches PgVectorMigrationService
  - grep -r '384' src/ returns no DB-related matches
Branch: kimi-coder/t_abc123-fix-pgvector-drift (pushed)
Reviewer: please verify index rebuild time on the 10k-row local dataset.
```

A bad `--result` string looks like:

```text
done
```

The coordinator needs enough to review without re-reading every file you changed.

---

## What you must NOT do

- Work on a task without claiming it
- Claim more than one task at a time (finish or release before claiming again)
- Complete or fail a task that is not yours
- Push to `main` directly
- Delete or update tasks (coordinator-only)
- Create the task queue schema manually (the CLI does it on first run)
- Install new npm packages without mentioning them in your result
- Modify `.agentdb/kimi-queue.js` itself unless the task explicitly asks you to
- Change CLAUDE.md, `.claude/agents/*`, or `.agentdb/*.md` unless the task explicitly asks
- Run `git config` or change git identity
- Commit your worker identity, API keys, or any machine-local data

---

## Cross-agent etiquette

Because multiple agents may be active at once:

1. **Claim quickly, fail fast.** Hold times of 5+ hours are suspicious. If you can't finish in a reasonable window, `release` and let someone else try.
2. **Read `list --status in_progress` before claiming** to see what everyone else is working on and avoid colliding on related files.
3. **If you touch a file another in-progress task is also touching, coordinate via `fail` + re-enqueue**, or pause and ask the coordinator.
4. **Commit trailers matter.** When the coordinator later reads git blame, it must be clear which agent wrote which line.

---

## What the coordinator does

You are not the coordinator unless you are `claude-code`. The coordinator:

- Writes new tasks with well-formed bodies
- Reviews `done` tasks and merges branches to `main`
- Triages `failed` tasks — re-enqueues, splits, abandons
- Updates or deletes stale tasks
- Runs `stats` regularly to spot stuck work
- Decides priorities

If you think a task should be re-prioritized, deleted, or rewritten, **do not do it yourself**. Leave a note in your `--result` or `--reason` and let the coordinator act.

---

## Reference

- Task queue CLI: [.agentdb/kimi-queue.js](kimi-queue.js)
- Protocol doc: [.agentdb/QUEUE.md](QUEUE.md)
- Project rules: [CLAUDE.md](../CLAUDE.md)
- Core coder agent definition: [.claude/agents/core/coder.md](../.claude/agents/core/coder.md)
- Infra task bundle: [docs/tasks/infra-db-migration/README.md](../docs/tasks/infra-db-migration/README.md)
- Student web task bundle: [tasks/student-web/README.md](../tasks/student-web/README.md)
- Student backend task bundle: [tasks/student-backend/README.md](../tasks/student-backend/README.md)

---

## Messaging (free-form communication between workers)

In addition to the task queue, you can send and receive messages via the same CLI. Messages are for **coordination**, not work assignment. Work assignment always goes through the task queue.

### When to send a message

- **Status update** to the coordinator mid-task: `kind=status`
- **Question** to the coordinator when the task body is ambiguous and you have a specific clarification need: `kind=question --correlation <tag>`
- **Note** to share context with another worker: `kind=note`
- **Ack** to confirm you received a directive: `kind=ack`

### When to receive messages

- **Before every `claim`** — check for coordination directives that might change priorities
- **During long-running tasks** — periodic recv to see if the coordinator is trying to redirect you
- **After completing a task** — see if the coordinator has follow-up feedback

### Basic send

```bash
node .agentdb/kimi-queue.js send --from <your-worker> --to <target-worker> \
  --kind status --subject "<short summary>" --body "<details>"
```

Link to a task when relevant:

```bash
node .agentdb/kimi-queue.js send --from <your-worker> --to claude-code \
  --kind status --subject "DB-00 blocked" \
  --body "docker compose up fails with: ..." --task t_95a77a446c72
```

### Basic receive

```bash
# Pull all direct messages
node .agentdb/kimi-queue.js recv --worker <your-worker>

# Pull coordination topic broadcasts too
node .agentdb/kimi-queue.js recv --worker <your-worker> --topic coordination

# Peek without clearing
node .agentdb/kimi-queue.js recv --worker <your-worker> --peek
```

### Question-answer pattern

When you need the coordinator's input mid-task, use `kind=question` with a correlation tag so the answer is easy to find:

```bash
# You (kimi-coder) ask:
node .agentdb/kimi-queue.js send --from kimi-coder --to claude-code \
  --kind question --subject "canonical answer" \
  --body "Should I use foo or bar?" --correlation q1

# Then periodically check for the answer:
node .agentdb/kimi-queue.js recv --worker kimi-coder --kind answer
# Look for the message with correlation_id=q1
```

Alternative for blocking questions: call `fail` on the task with the question in `--reason`. The coordinator sees the failure, answers, and re-enqueues. Use `fail` when you truly can't proceed; use `question` when you can keep working on other things while waiting.

### Heartbeat (declaring presence)

Lightweight way to say "I'm alive" without sending a message:

```bash
node .agentdb/kimi-queue.js heartbeat --worker <your-worker> --status active
```

Run this once per minute while idle so the coordinator's `workers --active` shows you as present.

### See who else is around

```bash
node .agentdb/kimi-queue.js workers --active
```

Lists every worker seen in the last 5 minutes. Useful before claiming parallel tasks — avoid colliding with another active worker.

### Messaging rules

1. **Always sign messages** with `--from <worker>`. Anonymous messages are not allowed.
2. **Do not send work assignments via messages.** If you need someone to do something, ask the coordinator to enqueue a task.
3. **Status per task, not per minute.** A status message every 5-10 minutes during a long task is reasonable. Every 30 seconds is spam.
4. **Questions need correlation IDs.** Short is fine: `q1`, `q2`, etc.
5. **Directives from `claude-code` on the `coordination` topic are authoritative.** Recv them before claiming new tasks.
6. **Do not ignore the coordinator.** If a directive arrives telling you to stop, stop.

---

## TL;DR

1. Identify yourself (`--worker`)
2. (First join) Complete your HANDSHAKE task — do not skip
3. Pull tasks (`next`, `claim`)
4. Read everything (`show`, linked files, CLAUDE.md)
5. Execute (edit, test, commit on a feature branch)
6. Report (`complete` or `fail` with a real result string)
7. Check messages (`recv`) before claiming the next task
8. Send status (`send --kind status`) for long-running tasks
9. Never merge to `main` — that's the coordinator's job
10. Loop

When in doubt, `fail` with a clear reason OR send a `kind=question` message and wait for the answer.
