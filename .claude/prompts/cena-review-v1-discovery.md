# Cena Comprehensive Review v1 — Discovery Pass (5 agents)

> **Use this version for first-pass audits on greenfield or untouched
> areas.** For re-verification runs, use `cena-review-v2-reverify.md` which
> adds a fix-verification preflight, child-safety/privacy compliance, and
> QA/regression authorities.

Orchestrate a 5-agent parallel audit of the Cena project. Spawn ALL 5 agents
in ONE message via Claude Code's Task tool with run_in_background: true. Do
NOT poll status after spawning — wait for results. Findings without evidence
are discarded.

## Stack ground truth (do not rediscover)

- Backend: .NET, DDD with bounded contexts, Marten (Postgres event store),
  NATS bus between Cena.Admin.Api.Host / Cena.Student.Api.Host and the Actor
  Host, SignalR for realtime. NOT GraphQL. NOT raw WebSockets.
- Admin UI: Vuexy Vue 3 at src/admin/full-version/, Firebase Auth project
  `cena-platform`, dev port 5174. Primary color #7367F0 is LOCKED.
- Student web: Vue, separate host.
- Mobile: Flutter (planned, not built — do not review Flutter code).
- Languages: English primary, Arabic + Hebrew secondary.
- Question bank: event-sourced, versioned, multi-lang as versions.
- Task queue: .agentdb/kimi-queue.db via `node .agentdb/kimi-queue.js`.

## Non-negotiable rules

1. NO stubs. Hardcoded happy-path responses = P0.
2. Labels must match the data.
3. Verify E2E: DB → API → UI.
4. Fix-forward: every P0/P1 becomes a queued task.
5. Root cause, not symptom.

## The 5 agents

1. **System & Contract Architect** — bounded contexts, REST/NATS/SignalR
   wiring, dead endpoints, stub detection.
2. **Security, Auth & Infra** — Firebase verification, tenant scoping,
   secrets, CORS, rate limits.
3. **Data, Performance & Projections** — Marten queries, N+1, indexes,
   event replay correctness.
4. **Pedagogy & Learning Science** — cited research only, no vibes.
5. **UX & Broken-Workflow Auditor** — Playwright click-through,
   label drift, silent failures.

(Full agent specs: see v2 — this v1 file is retained for history.)

## Outputs

- `docs/reviews/agent-<n>-<name>.md` per agent
- Findings follow the YAML schema in v2
- Each P0/P1 enqueued to `.agentdb/kimi-queue.db`
- Coordinator merges to `docs/reviews/cena-review-<date>.md`
