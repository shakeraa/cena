# Cena — E2E Flow Testing (multi-agent isolated workflows)

> Full-stack regression harness: Vue SPA → .NET API → Postgres/Marten → NATS → Stripe (test mode) → Firebase Auth (emulator). Every workflow proves the round-trip end-to-end, not one layer at a time.

## Why this exists

Unit + architecture tests catch code-level bugs. They miss **flow bugs**: a student logs in, picks Plus annual, Stripe webhook fires, the subscription actor processes it, the UI updates. Any of ~40 hops can silently break — an event sourced on the wrong stream, a projection not subscribed, a SignalR hub missing the class, a cached Firebase claim stale.

E2E flow tests exercise one user journey end-to-end at a time and assert at every boundary: DOM + DB + bus + Stripe.

## Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│  Playwright worker N (isolated tenant_id=t_e2e_<worker-id>)        │
│                                                                    │
│  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐      │
│  │  Fixture │───▶│ SPA in   │───▶│ student- │───▶│ Postgres │      │
│  │  setup   │    │ real     │    │ api      │    │ (Marten) │      │
│  │  (auth,  │    │ browser  │    │ host     │    │          │      │
│  │  tenant, │    └──────────┘    └─────┬────┘    └──────────┘      │
│  │  stripe) │                           │                           │
│  └──────────┘                           ▼                           │
│                                    ┌──────────┐    ┌──────────┐    │
│                                    │   NATS   │───▶│  actor-  │    │
│                                    │   bus    │    │  host    │    │
│                                    └──────────┘    └──────────┘    │
│                                                                    │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │  Test assertions run against ALL boundaries:                │    │
│  │    (a) DOM visible — `expect(page.getByTestId(...))`        │    │
│  │    (b) DB state  — Marten read via /admin/test/probe        │    │
│  │    (c) Bus — NATS subscribe during test, assert event seen  │    │
│  │    (d) Stripe — trigger-events.sh fires test-mode webhooks  │    │
│  └────────────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────────────┘
```

## Isolation model — shared stack, per-worker tenant

| Axis | Choice | Why |
|---|---|---|
| Stack | Shared `docker-compose up -d` (long-running) | Boot overhead is 90s+ per worker if not shared; test suite completes in ~2 min |
| Tenant | Per-worker `tenant_id = "t_e2e_{workerIndex}_{testRun}"` | Every write is tenant-scoped (ADR-0001); no cross-worker bleed |
| Firebase users | Fresh `e2e-{workerIndex}-{specName}@cena.test` per spec | No residual session state from prior runs |
| Stripe | Stripe **test mode** + `customer_{tenantId}` metadata tag | Webhook-level isolation via metadata match |
| NATS subjects | Durable consumer scoped to `cena.e2e.{tenantId}.>` | No cross-worker event leak on parent-digest / SignalR |
| DB truncate | NONE between tests; rely on tenant-scoping | Fast. Tests that need a blank slate create a new tenant. |

## Multi-agent run model

Two layers:

**Layer 1 — Playwright worker processes.** One command, N parallel workers on one machine:

```bash
cd src/student/full-version
npm run test:e2e:flow -- --workers=4
```

**Layer 2 — Claude sub-agents for autonomous regression triage.** When a workflow fails:

```
coordinator (this session)
  ├─ spawn sub-agent A → claim failing spec → read trace → propose fix in worktree → push branch
  ├─ spawn sub-agent B → run adjacent spec → confirm not affected by A's fix
  └─ spawn sub-agent C → independent workflow → keeps coverage hot during triage
```

Each sub-agent runs in its own git worktree (per CLAUDE.md convention) so their code edits don't collide, and each runs with a worker-id-isolated tenant so their Playwright processes don't see each other.

## Quick start

```bash
# 1. Ensure dev stack is up
docker compose -f docker-compose.yml -f docker-compose.app.yml up -d

# 2. Seed Firebase emulator users
docker exec cena-firebase-emulator /seed/seed-dev-users.sh

# 3. Bootstrap Stripe sandbox (one-time per dev)
./scripts/stripe/bootstrap-sandbox.sh

# 4. Run the e2e-flow suite (parallel)
cd src/student/full-version
npm run test:e2e:flow
```

## What's in this directory

- [TASK-E2E-001](TASK-E2E-001-subscription-happy-path.md) — login → pricing → Plus annual → Stripe checkout (test card success) → `/subscription/confirm` shows active
- [TASK-E2E-002](TASK-E2E-002-subscription-declined.md) — login → pricing → tier → decline card → `/subscription/cancel` with retry CTA
- [TASK-E2E-003](TASK-E2E-003-subscription-cancel-back.md) — login → pricing → tier → user dismisses Stripe modal → back to `/pricing` without a half-provisioned sub
- [TASK-E2E-004](TASK-E2E-004-multi-agent-runner.md) — how to fan N Claude sub-agents out for isolated triage

## Test code layout

```
src/student/full-version/
  playwright.e2e-flow.config.ts          # multi-worker, parallel, own testDir
  tests/e2e-flow/
    README.md                            # code-side runbook (fixtures, helpers)
    fixtures/
      tenant.ts                          # worker-scoped tenant id factory
      auth.ts                            # Firebase emulator sign-up/sign-in
      stripe.ts                          # Stripe test-card + webhook triggers
    probes/
      db-probe.ts                        # read-only Marten via /api/admin/test/probe
      bus-probe.ts                       # NATS JetStream ephemeral consumer
    workflows/
      subscription-happy-path.spec.ts    # TASK-E2E-001 (the spike — real, working)
      subscription-declined.spec.ts      # TASK-E2E-002 (scaffolded, TODO body)
      subscription-cancel-back.spec.ts   # TASK-E2E-003 (scaffolded, TODO body)
```

## Non-goals

- **Stripe's real hosted checkout UI.** We intercept the redirect and drive outcomes via `trigger-events.sh` — more reliable + faster than scripting `checkout.stripe.com`.
- **Load / performance testing.** Separate discipline; use k6 or similar.
- **Visual regression.** Existing `tests/e2e/rtl-visual-regression.spec.ts` owns that surface.

## Related

- [PRR-428](../pre-release-review/TASK-PRR-428-notifications-di-wiring.md) — notifications DI (landed)
- [PRR-429](../pre-release-review/TASK-PRR-429-meta-cloud-whatsapp.md) — Meta WhatsApp sender (landed)
- [EPIC-PRR-I](../pre-release-review/EPIC-PRR-I-subscription-pricing-model.md) — subscription aggregate (landed)
- [ADR-0057](../../docs/adr/0057-subscription-aggregate-retail-pricing.md) — subscription DDD
- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — tenant isolation (the leg this suite relies on)
