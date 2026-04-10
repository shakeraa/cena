# Claude Code Configuration - RuFlo V3

> **Cross-agent note**: The sibling file [AGENTS.md](AGENTS.md) at the repo root is the cross-agent onboarding entry point (read automatically by Kimi Code, Codex CLI, and other CLIs that respect the `AGENTS.md` convention). It contains the full 9-step first-session checklist for non-Claude agents joining the shared task queue and mirrors the multi-agent coordination rules in this file. Keep the two in sync when changing coordination protocol — update both, not one.

## Session Start (Always Run First)

At the start of every new conversation, read all memory files listed in `~/.claude/projects/-Users-shaker-edu-apps-cena/memory/MEMORY.md` before doing anything else. This ensures context about the user, prior decisions, and project state is loaded immediately.

Also check for new inter-agent messages addressed to `claude-code` before starting substantive work:

```bash
node .agentdb/kimi-queue.js recv --worker claude-code --peek
node .agentdb/kimi-queue.js workers --active
```

The `--peek` flag leaves messages unconsumed so you can decide whether to act on them before auto-acking. If other workers (Kimi, sub-agents, human operators) have questions or status updates waiting, address them before claiming new work.

## Behavioral Rules (Always Enforced)

- Do what has been asked; nothing more, nothing less
- NEVER create files unless they're absolutely necessary for achieving your goal
- ALWAYS prefer editing an existing file to creating a new one
- NEVER proactively create documentation files (*.md) or README files unless explicitly requested
- NEVER save working files, text/mds, or tests to the root folder
- Never continuously check status after spawning a swarm — wait for results
- ALWAYS read a file before editing it
- NEVER commit secrets, credentials, or .env files

## File Organization

- NEVER save to root folder — use the directories below
- Use `/src` for source code files
- Use `/tests` for test files
- Use `/docs` for documentation and markdown files
- Use `/config` for configuration files
- Use `/scripts` for utility scripts
- Use `/examples` for example code

## Project Architecture

- Follow Domain-Driven Design with bounded contexts
- Keep files under 500 lines
- Use typed interfaces for all public APIs
- Prefer TDD London School (mock-first) for new code
- Use event sourcing for state changes
- Ensure input validation at system boundaries

### Project Config

- **Topology**: hierarchical-mesh
- **Max Agents**: 15
- **Memory**: hybrid
- **HNSW**: Enabled
- **Neural**: Enabled

## Build & Test

```bash
# Build
npm run build

# Test
npm test

# Lint
npm run lint
```

- ALWAYS run tests after making code changes
- ALWAYS verify build succeeds before committing

## Security Rules

- NEVER hardcode API keys, secrets, or credentials in source files
- NEVER commit .env files or any file containing secrets
- Always validate user input at system boundaries
- Always sanitize file paths to prevent directory traversal
- Run `npx @claude-flow/cli@latest security scan` after security-related changes

## Concurrency: 1 MESSAGE = ALL RELATED OPERATIONS

- All operations MUST be concurrent/parallel in a single message
- Use Claude Code's Task tool for spawning agents, not just MCP
- ALWAYS batch ALL todos in ONE TodoWrite call (5-10+ minimum)
- ALWAYS spawn ALL agents in ONE message with full instructions via Task tool
- ALWAYS batch ALL file reads/writes/edits in ONE message
- ALWAYS batch ALL Bash commands in ONE message

## Swarm Orchestration

- MUST initialize the swarm using CLI tools when starting complex tasks
- MUST spawn concurrent agents using Claude Code's Task tool
- Never use CLI tools alone for execution — Task tool agents do the actual work
- MUST call CLI tools AND Task tool in ONE message for complex work

### 3-Tier Model Routing (ADR-026)

| Tier | Handler | Latency | Cost | Use Cases |
|------|---------|---------|------|-----------|
| **1** | Agent Booster (WASM) | <1ms | $0 | Simple transforms (var→const, add types) — Skip LLM |
| **2** | Haiku | ~500ms | $0.0002 | Simple tasks, low complexity (<30%) |
| **3** | Sonnet/Opus | 2-5s | $0.003-0.015 | Complex reasoning, architecture, security (>30%) |

- Always check for `[AGENT_BOOSTER_AVAILABLE]` or `[TASK_MODEL_RECOMMENDATION]` before spawning agents
- Use Edit tool directly when `[AGENT_BOOSTER_AVAILABLE]`

## Swarm Configuration & Anti-Drift

- ALWAYS use hierarchical topology for coding swarms
- Keep maxAgents at 6-8 for tight coordination
- Use specialized strategy for clear role boundaries
- Use `raft` consensus for hive-mind (leader maintains authoritative state)
- Run frequent checkpoints via `post-task` hooks
- Keep shared memory namespace for all agents

```bash
npx @claude-flow/cli@latest swarm init --topology hierarchical --max-agents 8 --strategy specialized
```

## Swarm Execution Rules

- ALWAYS use `run_in_background: true` for all agent Task calls
- ALWAYS put ALL agent Task calls in ONE message for parallel execution
- After spawning, STOP — do NOT add more tool calls or check status
- Never poll TaskOutput or check swarm status — trust agents to return
- When agent results arrive, review ALL results before proceeding

## V3 CLI Commands

### Core Commands

| Command | Subcommands | Description |
|---------|-------------|-------------|
| `init` | 4 | Project initialization |
| `agent` | 8 | Agent lifecycle management |
| `swarm` | 6 | Multi-agent swarm coordination |
| `memory` | 11 | AgentDB memory with HNSW search |
| `task` | 6 | Task creation and lifecycle |
| `session` | 7 | Session state management |
| `hooks` | 17 | Self-learning hooks + 12 workers |
| `hive-mind` | 6 | Byzantine fault-tolerant consensus |

### Quick CLI Examples

```bash
npx @claude-flow/cli@latest init --wizard
npx @claude-flow/cli@latest agent spawn -t coder --name my-coder
npx @claude-flow/cli@latest swarm init --v3-mode
npx @claude-flow/cli@latest memory search --query "authentication patterns"
npx @claude-flow/cli@latest doctor --fix
```

## Available Agents (60+ Types)

### Core Development
`coder`, `reviewer`, `tester`, `planner`, `researcher`

### Specialized
`security-architect`, `security-auditor`, `memory-specialist`, `performance-engineer`

### Swarm Coordination
`hierarchical-coordinator`, `mesh-coordinator`, `adaptive-coordinator`

### GitHub & Repository
`pr-manager`, `code-review-swarm`, `issue-tracker`, `release-manager`

### SPARC Methodology
`sparc-coord`, `sparc-coder`, `specification`, `pseudocode`, `architecture`

## Memory Commands Reference

```bash
# Store (REQUIRED: --key, --value; OPTIONAL: --namespace, --ttl, --tags)
npx @claude-flow/cli@latest memory store --key "pattern-auth" --value "JWT with refresh" --namespace patterns

# Search (REQUIRED: --query; OPTIONAL: --namespace, --limit, --threshold)
npx @claude-flow/cli@latest memory search --query "authentication patterns"

# List (OPTIONAL: --namespace, --limit)
npx @claude-flow/cli@latest memory list --namespace patterns --limit 10

# Retrieve (REQUIRED: --key; OPTIONAL: --namespace)
npx @claude-flow/cli@latest memory retrieve --key "pattern-auth" --namespace patterns
```

## Quick Setup

```bash
claude mcp add claude-flow -- npx -y @claude-flow/cli@latest
npx @claude-flow/cli@latest daemon start
npx @claude-flow/cli@latest doctor --fix
```

## Claude Code vs CLI Tools

- Claude Code's Task tool handles ALL execution: agents, file ops, code generation, git
- CLI tools handle coordination via Bash: swarm init, memory, hooks, routing
- NEVER use CLI tools as a substitute for Task tool agents

## Multi-Agent Coordination (Shared Task Queue)

This repo runs multiple coding agents in parallel — Claude Code (this session), Claude sub-agents via the Task tool, Kimi Code CLI, and human operators. They coordinate via a shared SQLite-backed task queue so no two agents duplicate work and every completed task lands under a named branch that the coordinator reviews before merging to `main`.

**Canonical queue**: `.agentdb/kimi-queue.db` (auto-created on first CLI call)
**CLI**: `node .agentdb/kimi-queue.js`
**Cross-agent entry point**: [AGENTS.md](AGENTS.md) — what Kimi and other CLIs read on session start
**Protocol doc**: [.agentdb/QUEUE.md](.agentdb/QUEUE.md) — **read this before assigning or claiming any task**
**Coder agent instructions**: [.agentdb/AGENT_CODER_INSTRUCTIONS.md](.agentdb/AGENT_CODER_INSTRUCTIONS.md) — required reading for any agent that wants to pull work from the queue

### Golden rules

- **Claude Code main session** is the default coordinator. Worker name: `claude-code`. Owns enqueue, review, merge-to-main.
- **Every worker** identifies itself with a consistent `--worker <name>` flag (`kimi-coder`, `claude-subagent-<purpose>`, `human-<name>`, etc.).
- **No worker pushes to `main` directly.** Push feature branches named `<worker>/<task-id>-<slug>`; the coordinator reviews and merges.
- **Claim before working.** An in-progress task is held by exactly one worker; the atomic claim is enforced by SQLite.
- **Fail fast and loudly.** If a task blocks, call `fail` with a reason; do not silently drop.
- **Never delete or update tasks** unless you are the coordinator.

### Essential commands

```bash
# Peek at the next task for your identity
node .agentdb/kimi-queue.js next --assignee kimi-coder

# Claim it atomically
node .agentdb/kimi-queue.js claim <id> --worker kimi-coder

# Read the full task body (goal, files, DoD, reporting requirements)
node .agentdb/kimi-queue.js show <id>

# Report
node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<summary+branch>"
# or
node .agentdb/kimi-queue.js fail <id> --worker kimi-coder --reason "<blocker>"

# Inspect the whole queue
node .agentdb/kimi-queue.js list --status pending
node .agentdb/kimi-queue.js stats
```

### When spawning a sub-agent via the Task tool

Pass the task ID and worker name in the agent prompt so the sub-agent knows which row to claim and what identity to use. Every sub-agent prompt that operates on a queued task MUST include:

1. A directive to read [.agentdb/AGENT_CODER_INSTRUCTIONS.md](.agentdb/AGENT_CODER_INSTRUCTIONS.md) before anything else
2. The task ID to claim
3. The worker name it should use (e.g. `claude-subagent-db00`)
4. An instruction to report via `complete` or `fail` before returning

### Inter-agent messaging

Beyond the task queue, workers can send and receive free-form messages for coordination (status updates, questions, directives, broadcasts). Same CLI, same DB, different tables.

```bash
# Send a direct message
node .agentdb/kimi-queue.js send --from <you> --to <target> \
  --kind status|question|answer|note|directive --subject "<x>" --body "<y>"

# Receive (auto-acks by default, --peek to leave unconsumed)
node .agentdb/kimi-queue.js recv --worker <you> [--topic <t>] [--kind ...] [--peek]

# Broadcast to a topic
node .agentdb/kimi-queue.js send --from <you> --topic coordination --kind directive --body "..."

# Heartbeat (declare presence without sending anything)
node .agentdb/kimi-queue.js heartbeat --worker <you> --status active

# See who else is active
node .agentdb/kimi-queue.js workers --active
```

Messaging is pull-based — no push notifications. Workers should `recv` before every task claim and periodically during long tasks. Directives broadcast on the `coordination` topic by `claude-code` are authoritative.

**Rule**: messages are for **coordination**, not **work assignment**. Work always goes through the task queue. Never use a message to tell someone to do something substantive.

### Worktree isolation (multi-agent parallelism)

When Claude Code spawns a sub-agent to work on a queued task, the sub-agent operates in its own git worktree so it does not collide with the main session's file edits. Worktree convention:

- Claude sub-agents → `.claude/worktrees/<task-id>/` (git-ignored)
- Kimi and external workers → `.agentdb/worktrees/<task-id>/` (git-ignored)
- Claude Code main session (coordinator role) → the main repo root

The sub-agent spawn prompt must instruct the sub-agent to:

1. `git worktree add .claude/worktrees/<task-id> -b claude-subagent-<purpose>/<task-id>-<slug> origin/main`
2. `cd` into the worktree
3. Do all work there
4. Commit, push, then `node .agentdb/kimi-queue.js complete <task-id> --worker claude-subagent-<purpose> --result ...`
5. Return to the main worktree before exiting

The main Claude Code session reviews completed branches and merges to `main` from the main worktree. After merge:

```bash
git worktree remove .claude/worktrees/<task-id>
```

If the coordinator also takes on a specific task itself (not just coordinating), it should also use a worktree under `.claude/worktrees/` to keep the main worktree clean for review/merge work.

Full protocol: [.agentdb/QUEUE.md](.agentdb/QUEUE.md) and [AGENTS.md](AGENTS.md).

### Handshake before real work (new workers only)

Every new worker (Kimi Code CLI, a second Claude Code session, a new sub-agent identity) must complete a **handshake task** before claiming any real work. The handshake is zero side-effects and proves the worker can read the protocol, use the CLI, and echo a rotating per-handshake phrase unique to its own task body.

Coordinator enqueues a handshake:

```bash
node .agentdb/kimi-queue.js handshake <worker-name>
# Prints: task-id + 8-byte rotating phrase (unique per handshake)
```

Worker claims, reads the body (which contains the phrase), completes with a result string containing `phrase=<value>,worker=<name>,node=<ver>,os=<sys>,files-read=3/3,ready=true`.

Coordinator verifies:

```bash
node .agentdb/kimi-queue.js verify <task-id>
# Exit 0 = PASS, Exit 3 = FAIL
```

Advisory-only: the CLI does not block real claims, but the coordinator's convention is to only greenlight real work for workers with a passing handshake on record. Protocol details: [.agentdb/QUEUE.md](.agentdb/QUEUE.md#handshake-first-join-ceremony)

## Support

- Documentation: https://github.com/ruvnet/claude-flow
- Issues: https://github.com/ruvnet/claude-flow/issues
