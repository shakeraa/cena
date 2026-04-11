# AGENTS.md — Cena Multi-Agent Onboarding

> **Read this file first.** This file is the **self-contained entry point** for any non-Claude AI coding agent or human operator joining the Cena repo. It follows the emerging cross-agent `AGENTS.md` convention so that Kimi Code, Codex CLI, Cursor, Aider, and any other CLI that respects this convention will pick it up automatically on session start.

**Claude Code** has its own file at `CLAUDE.md` which it reads automatically. **Non-Claude agents must NOT read CLAUDE.md** — it is Claude-Code-specific configuration. Every rule you need is **inlined in this file** plus the two required reading docs listed below. CLAUDE.md is for Claude Code, AGENTS.md is for you.

---

## Who this is for

| If you are | Required reading |
|---|---|
| **Kimi Code** | This file + [.agentdb/AGENT_CODER_INSTRUCTIONS.md](.agentdb/AGENT_CODER_INSTRUCTIONS.md) + [.agentdb/QUEUE.md](.agentdb/QUEUE.md). **Do not read CLAUDE.md.** |
| **Codex CLI / Cursor / Aider / other** | This file + [.agentdb/AGENT_CODER_INSTRUCTIONS.md](.agentdb/AGENT_CODER_INSTRUCTIONS.md) + [.agentdb/QUEUE.md](.agentdb/QUEUE.md). **Do not read CLAUDE.md.** |
| **Human operator** | This file (orient yourself) + whichever task bundle you are driving |
| **Claude Code main session** | CLAUDE.md — this file exists for other agents, not you |
| **Claude Code sub-agent via Task tool** | CLAUDE.md + your spawn prompt + the task body you were given |

If you are a non-Claude CLI agent and you skipped the required reading, stop now and go read it. Skipping breaks the coordination protocol and will cause your work to be rejected by the coordinator.

---

## What this repo is

Cena is an event-sourced adaptive learning platform (Marten on PostgreSQL 16 + pgvector, Proto.Actor cluster, NATS bus, Vue 3 Vuexy admin + student web, Flutter mobile, .NET 9 backend). Multiple agents work on it in parallel. There is a shared task queue so nobody steps on anyone else's work.

**Repository root**: `/Users/shaker/edu-apps/cena`
**Task queue**: `.agentdb/kimi-queue.db` (SQLite, WAL mode, safe for concurrent access)
**Task queue CLI**: `node .agentdb/kimi-queue.js`
**Full protocol**: [.agentdb/QUEUE.md](.agentdb/QUEUE.md)
**Coder agent instructions**: [.agentdb/AGENT_CODER_INSTRUCTIONS.md](.agentdb/AGENT_CODER_INSTRUCTIONS.md)

---

## The coordination model in one sentence

**Work is pulled from the queue, results are reported to the queue, messages are sent through the queue, and `main` is only ever merged by the coordinator (`claude-code`).**

```
┌─────────────────┐       task queue         ┌──────────────────┐
│  Coordinator    │  ──▶  enqueue tasks  ──▶ │  Workers (you)   │
│  (claude-code)  │  ◀── complete/fail  ──   │  kimi-coder,     │
│                 │  ◀── status/question ─   │  claude-subagent,│
│  reviews +      │   ──▶ directives    ──▶  │  human, etc.     │
│  merges to main │                          │                  │
└─────────────────┘                          └──────────────────┘
         ▲                                             │
         │                                             │
         └──────── feature branches (git) ─────────────┘
              (workers push, coordinator reviews
               and merges — never direct to main)
```

---

## Onboarding: your first session

If this is your first time on this repo **as a specific worker identity**, you MUST complete the onboarding before touching any real code. The protocol has a **handshake** step specifically to prove that you read the docs and can use the CLI correctly.

### Worker identity

Pick one name for your entire session and use it consistently as `--worker <name>` on every CLI call.

| You are | Use worker name |
|---|---|
| Kimi Code main session | `kimi-coder` |
| Kimi Code in review mode | `kimi-reviewer` |
| Claude Code main session | `claude-code` (coordinator role) |
| Claude Code Task-tool sub-agent | `claude-subagent-<purpose>` (e.g. `claude-subagent-db00`) |
| Codex CLI | `codex-<role>` |
| Human operator | `human-<yourname>` |

---

## 9-step first-session checklist (MANDATORY)

Run every step, in order, before claiming any real task. If any step errors, stop and either ask the coordinator (via a message, see step 1) or call `fail` on the handshake task with a specific reason.

### STEP 0 — Read the docs (no shortcuts)

```text
1. AGENTS.md                               (this file, ~400 lines)
2. .agentdb/AGENT_CODER_INSTRUCTIONS.md    (~300 lines)
3. .agentdb/QUEUE.md                       (~500 lines)
```

These are short on purpose. You cannot coordinate correctly without them. **Do NOT read CLAUDE.md** — that is Claude-Code-specific configuration and is not yours to follow. Every rule you need is in the three files above.

### STEP 1 — Check for messages from the coordinator

```bash
cd /Users/shaker/edu-apps/cena
node .agentdb/kimi-queue.js recv --worker <your-worker-name>
```

There is likely a welcome `directive` from `claude-code` waiting for you. Read it.

### STEP 2 — Heartbeat so the coordinator sees you online

```bash
node .agentdb/kimi-queue.js heartbeat --worker <your-worker-name> --status active
```

### STEP 3 — Find your handshake task

```bash
node .agentdb/kimi-queue.js list --assignee <your-worker-name>
```

Look for a task with `tag=handshake` and `priority=critical`. It will be titled `HANDSHAKE: <your-worker-name>`. If no handshake task exists, send a message to the coordinator asking for one:

```bash
node .agentdb/kimi-queue.js send --from <your-worker-name> --to claude-code \
  --kind question --subject "need handshake" \
  --body "No handshake task found for worker '<your-worker-name>'. Please enqueue one." \
  --correlation <your-worker-name>-init
```

### STEP 4 — Claim the handshake atomically

```bash
node .agentdb/kimi-queue.js claim <handshake-task-id> --worker <your-worker-name>
```

The claim is a single SQL UPDATE — if someone else already has it, you get a loud conflict error.

### STEP 5 — Show the handshake and extract your PHRASE

```bash
node .agentdb/kimi-queue.js show <handshake-task-id>
```

Inside the body there is a line:

```text
PHRASE: <8-byte hex value>
```

That phrase is **unique to your handshake**. It is not in any other file, any other doc, any memory. Do not substitute a phrase from a previous handshake or a friend's handshake — you will fail verification.

### STEP 6 — Environment sanity check

```bash
node -e "require('better-sqlite3'); console.log('ok')"
node --version
uname -s
```

If `better-sqlite3` fails to load, your environment is broken. Do not proceed. Call fail:

```bash
node .agentdb/kimi-queue.js fail <handshake-task-id> --worker <your-worker-name> \
  --reason "better-sqlite3 not installed; cannot access queue"
```

And tell the human operator.

### STEP 7 — Complete the handshake

Your `--result` string MUST contain all six fields, comma-separated, no spaces around the commas:

- `phrase=<the exact PHRASE from YOUR task body>`
- `worker=<your-worker-name>`
- `node=<output of node --version>`
- `os=<output of uname -s>` (e.g. `Darwin`, `Linux`)
- `files-read=3/3`
- `ready=true`

Example:

```bash
node .agentdb/kimi-queue.js complete <handshake-task-id> --worker <your-worker-name> \
  --result "phrase=a090703865b5ec6b,worker=kimi-coder,node=v20.11.0,os=Darwin,files-read=3/3,ready=true"
```

### STEP 8 — Send a status message to the coordinator

```bash
node .agentdb/kimi-queue.js send --from <your-worker-name> --to claude-code \
  --kind status --subject "handshake submitted" \
  --body "Handshake completed. Standing by for verification and go-ahead directive." \
  --task <handshake-task-id>
```

### STEP 9 — STOP and wait for the coordinator's go-ahead

**Do not claim any other task** until the coordinator runs `verify` on your handshake and sends you a directive on the `coordination` topic. Poll every minute:

```bash
# Your direct inbox
node .agentdb/kimi-queue.js recv --worker <your-worker-name>

# Coordination broadcasts
node .agentdb/kimi-queue.js recv --worker <your-worker-name> --topic coordination
```

When a `kind=directive` message arrives from `claude-code` with a subject like "greenlit" or "start real work", you are cleared to begin pulling real tasks.

---

## After onboarding: the normal work loop

Once you are greenlit, the loop is:

```bash
# 1. Check messages and directives first
node .agentdb/kimi-queue.js recv --worker <you>
node .agentdb/kimi-queue.js recv --worker <you> --topic coordination

# 2. Heartbeat
node .agentdb/kimi-queue.js heartbeat --worker <you>

# 3. Peek at the next task
node .agentdb/kimi-queue.js next --assignee <you>

# 4. Claim it
node .agentdb/kimi-queue.js claim <id> --worker <you>

# 5. Show and read the body fully
node .agentdb/kimi-queue.js show <id>

# 6. Do the work
#    - Create a feature branch named <your-worker>/<task-id>-<slug>
#    - Read every file the body references BEFORE editing
#    - Commit in small atomic units with a Co-Authored-By trailer identifying you
#    - Run lint and tests locally before completing
#    - Push the branch (do NOT open a PR, do NOT merge to main)

# 7. Mid-task: send status updates every 5-10 minutes on long tasks
node .agentdb/kimi-queue.js send --from <you> --to claude-code \
  --kind status --subject "<short>" --body "<details>" --task <id>

# 8. Complete (or fail with a reason)
node .agentdb/kimi-queue.js complete <id> --worker <you> \
  --result "<summary, files, test results, branch>"
# OR
node .agentdb/kimi-queue.js fail <id> --worker <you> --reason "<blocker>"

# 9. Loop
```

---

## Project architecture rules (apply to ALL code you write)

These are the Cena-wide rules. They are inlined here so you do not need to read CLAUDE.md.

### File organization

- **NEVER save files to the repo root folder.** Working files, markdown, tests, scripts — none of them go at the root.
- Use `/src` for source code
- Use `/tests` for test files
- Use `/docs` for documentation and markdown
- Use `/config` for configuration files
- Use `/scripts` for utility scripts
- Use `/examples` for example code
- Existing exceptions (do not add new root files without coordinator approval): `CLAUDE.md`, `AGENTS.md`, `README.md`, `package.json`, `.gitignore`, `/tasks/` directory tree

### Architecture

- **Follow Domain-Driven Design** with bounded contexts
- **Keep files under 500 lines.** If you are about to blow past 500, split the file into focused units.
- **Use typed interfaces** for all public APIs (TypeScript `interface`, C# `interface`, Dart `abstract class`, etc.)
- **Prefer TDD London School** (mock-first) for new code
- **Use event sourcing** for state changes in the backend (Marten events, upcasters for evolution)
- **Validate all input at system boundaries** (HTTP handlers, NATS consumers, actor message handlers)
- **Sanitize file paths** to prevent directory traversal

### Build and test

Whenever a task touches code, you MUST:

1. Run the relevant build command before completing (`npm run build`, `dotnet build`, `flutter build`, etc.)
2. Run the relevant test suite (`npm test`, `dotnet test`, `pytest`, `flutter test`)
3. Run the linter (`npm run lint`, `dotnet format`, `dart format`)
4. Report the status of all three in your `--result` string

Never call `complete` on a task with broken build, failing tests, or new lint errors unless the task body explicitly accepts that state.

**Student web (`src/student/full-version/`) install recipe**: on a fresh clone, run `npm install && npm run dev`. The package's `postinstall` is gated by `scripts/postinstall-guard.mjs`, which cannot crash the install even if the optional icon/MSW bootstrap fails; a separate `predev` hook verifies every required artifact before Vite starts and prints a clear recovery recipe if anything is missing. If you ever see `sh: vite: command not found`, run `rm -rf node_modules package-lock.json && npm install` from the package directory. See [src/student/full-version/README.md](src/student/full-version/README.md) for the full recovery flow (FIND-ux-001).

### Security

- **NEVER hardcode API keys, secrets, or credentials** in source files
- **NEVER commit `.env` files** or anything matching `*.env`, `secrets/`, `credentials.json`, `serviceAccountKey.json`, `service-account-key.json`
- **Always validate user input** at system boundaries
- **Always sanitize file paths** to prevent directory traversal
- If you see a secret in a file, do not commit it; send a `kind=note` message to `claude-code` flagging it and move on

### Git discipline

- **Create a feature branch** named `<your-worker>/<task-id>-<slug>`, e.g. `kimi-coder/t_abc123-fix-pgvector-drift`
- **Commit in small atomic units**, not one giant "work done" commit
- **Identify yourself** in every commit message via a `Co-Authored-By:` trailer that includes your worker name
- **Push your branch** to the remote after completing (`git push -u origin <branch>`)
- **Do NOT open a pull request** and **do NOT merge to main** — the coordinator handles review and merge
- **NEVER update the git config** (name, email, signing keys) — it is not yours to change
- **NEVER force push** unless the task body explicitly authorizes it
- **NEVER skip hooks** (`--no-verify`, `--no-gpg-sign`) — if a hook fails, investigate and fix the cause

### Worktrees (isolated workspaces — REQUIRED for all non-trivial tasks)

To prevent collisions between parallel workers (claude-code, kimi-coder, sub-agents), every worker operates in its own **git worktree**. The worktrees share the single `.git/` database at the repo root but each has its own on-disk working directory, its own checked-out branch, and its own build artifacts. Multiple workers can claim and execute different tasks at the same time without touching each other's files.

**Convention**:

| Worker | Worktree root |
|---|---|
| `claude-code` (coordinator) | The main repo at `/Users/shaker/edu-apps/cena` |
| `claude-subagent-*` | `.claude/worktrees/<task-id>/` |
| `kimi-coder` and other Kimi workers | `.agentdb/worktrees/<task-id>/` |
| Other external workers | `.agentdb/worktrees/<task-id>/` |

Both worktree roots are git-ignored (see `.gitignore`), so the directories themselves never show up in `git status` of the main worktree.

**Worker workflow**:

```bash
# 1. After claim, create a worktree from main with a fresh branch
cd /Users/shaker/edu-apps/cena   # main worktree
git fetch origin
git worktree add .agentdb/worktrees/<task-id> -b <worker>/<task-id>-<slug> origin/main

# 2. Move into your worktree
cd .agentdb/worktrees/<task-id>

# 3. Do the work IN this worktree — all edits, builds, tests happen here.
#    Do not `cd` back to the main worktree during the task.

# 4. When done, commit and push FROM the worktree
git add -A
git commit -m "feat(scope): short subject

Task: <task-id>
<body>

Co-Authored-By: <worker-name> <<worker>@cena.local>"
git push -u origin <worker>/<task-id>-<slug>

# 5. Return to main worktree and mark the task complete via the queue CLI
cd /Users/shaker/edu-apps/cena
node .agentdb/kimi-queue.js complete <task-id> --worker <worker-name> \
  --result "<summary including branch name>"

# 6. The coordinator will review the branch and merge. After merge lands on
#    main, the coordinator removes the worktree:
git worktree remove .agentdb/worktrees/<task-id>
```

**Rules**:

- **Always create a fresh worktree from `origin/main`.** Never reuse a worktree across tasks. A new worktree per task prevents cross-task contamination.
- **Never edit files in the main worktree** unless you are explicitly the coordinator handling review/merge.
- **Never `git worktree add` from inside another worktree** — always from the main repo root.
- **Never commit the worktree directory itself** (the `.gitignore` already excludes it).
- **Do not prune or remove other workers' worktrees.** If a worktree looks stale, send a message to the coordinator.
- **Run your full build + test suite inside the worktree** before completing. Do not shortcut by building in the main tree.
- **If you create files that should land in the repo, they go under the worktree path**, not under the main worktree path.

**Verifying your isolation**:

```bash
git worktree list
# Should show: the main worktree + only your own worktree, nothing else unexpected
```

If you see another worker's worktree in the list, that is normal — worktrees are shared via the single `.git/` directory. Do not touch them.

**Trivial exception**: single-line edits on the main branch (typo fixes, comment-only changes) may skip the worktree step if the task body explicitly says "trivial, no worktree required". Otherwise, worktrees are mandatory.

---

## Non-negotiable rules

Any violation breaks trust in the coordination protocol and gets your worker identity revoked.

1. **Always identify yourself** with `--worker <name>` on every CLI call.
2. **Never work on a task you haven't claimed.** The claim is atomic; trust it.
3. **Never push to `main` directly.** Push feature branches; the coordinator merges.
4. **Never modify** `CLAUDE.md`, `AGENTS.md`, `.claude/*`, `.agentdb/*.md`, or `.agentdb/kimi-queue.js` unless a task body explicitly requires it.
5. **Never read `CLAUDE.md`** — it is Claude-Code-specific. Your rules are in this file.
6. **Never delete or update queued tasks** — coordinator-only.
7. **Never commit secrets**, `.env` files, or credentials.
8. **Never save working files to the root folder.** Use `/src`, `/tests`, `/docs`, `/config`, `/scripts`, `/examples`.
9. **Never create new files** unless the task explicitly requires it. Prefer editing existing files.
10. **Never install new npm packages** without mentioning them in your `--result` string.
11. **Never skip hooks** (`--no-verify`, `--no-gpg-sign`). If a hook fails, fix the cause.
12. **Never force-push** unless the task body explicitly authorizes it.
13. **Claim once per state transition.** Don't call `claim` twice, don't `complete` a done task, don't edit a task body while you own it.
14. **Fail loudly, not silently.** If you can't finish, call `fail` with a specific actionable reason.
15. **Read every file** the task body references before you edit anything. No skimming.
16. **Respect priority order.** `critical > high > normal > low`. Within the same priority, FIFO.
17. **Respect the coordinator.** If a directive tells you to stop, stop. If a question is open, wait for the answer.
18. **Follow the architecture rules above** — DDD, files under 500 lines, typed interfaces, input validation, event sourcing for state changes.

---

## Messaging reference (quick)

Full details in [.agentdb/QUEUE.md](.agentdb/QUEUE.md). Most common patterns:

```bash
# Status update during a long task
node .agentdb/kimi-queue.js send --from <you> --to claude-code \
  --kind status --subject "<summary>" --body "<details>" --task <task-id>

# Ask the coordinator a question mid-task (you can keep working on other things while waiting)
node .agentdb/kimi-queue.js send --from <you> --to claude-code \
  --kind question --subject "<summary>" --body "<details>" --correlation <tag>

# Look for the answer later
node .agentdb/kimi-queue.js recv --worker <you> --kind answer

# Broadcast a learning/tip to everyone
node .agentdb/kimi-queue.js send --from <you> --topic learnings \
  --kind note --subject "<summary>" --body "<details>"

# Declare presence without sending anything
node .agentdb/kimi-queue.js heartbeat --worker <you>

# See who else is active right now
node .agentdb/kimi-queue.js workers --active
```

---

## Where the work is

| Task bundle | Location | Who it's for |
|---|---|---|
| Infra: DB migration + host split | [docs/tasks/infra-db-migration/](docs/tasks/infra-db-migration/README.md) | Backend / DevOps |
| Student web UI | [tasks/student-web/](tasks/student-web/README.md) | Frontend |
| Student backend | [tasks/student-backend/](tasks/student-backend/README.md) | Backend |
| Student AI interaction (legacy) | [docs/tasks/student-ai-interaction/](docs/tasks/student-ai-interaction/README.md) | Backend (mostly done) |

Every task file under those folders has a full scope, files-to-read list, definition of done, and risks. The task queue enqueues them one at a time with a compact body that points at the full spec.

---

## If you hit trouble

1. **Task body is ambiguous** → send a `kind=question` message with a correlation tag, keep doing other work, check for the answer later.
2. **Environment blocker** (missing package, broken build, Git conflict) → `fail` the task with a specific reason; do not guess or improvise.
3. **You need to coordinate with another active worker** → send a direct message, do not silently overlap their work.
4. **You think a task should be re-prioritized or rewritten** → do NOT touch it; send a `kind=question` to the coordinator and wait.
5. **You noticed a drift or bug outside your current task** → send a `kind=note` to `claude-code` with details; do not fix it inside an unrelated task.

---

## TL;DR

1. Read AGENTS.md (this file), .agentdb/AGENT_CODER_INSTRUCTIONS.md, .agentdb/QUEUE.md
2. **Do NOT read CLAUDE.md** — it is Claude-Code-specific
3. Pick your worker name, stick with it
4. Complete the handshake FIRST — do not skip
5. Wait for the coordinator's greenlight directive
6. Loop: recv → heartbeat → next → claim → show → do → complete → repeat
7. Branch, don't merge. The coordinator merges.
8. Message for coordination, never for work assignment.
9. Fail loudly, ask questions, never guess silently.
10. Follow the architecture + security + git rules inlined above — they are the full set, no hidden rules elsewhere.
