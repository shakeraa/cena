# Cena E2E flow tests — code-side runbook

> Sibling to `tests/e2e/` (UI-only). This directory contains **full-stack workflow** tests — SPA → backend → DB → bus — with multi-worker isolation.

## Prerequisites

1. **Dev stack running**:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.app.yml up -d
   ```
2. **Firebase emulator seeded** (one-time per emulator lifetime):
   ```bash
   docker exec cena-firebase-emulator /seed/seed-dev-users.sh
   ```
3. **Stripe CLI** logged into test mode (one-time per dev machine):
   ```bash
   stripe login
   ./scripts/stripe/bootstrap-sandbox.sh
   ```

## Running

```bash
# From src/student/full-version
npm run test:e2e:flow                      # default: 4 workers
npm run test:e2e:flow -- --workers=1       # serial debug
npm run test:e2e:flow -- --grep happy      # filter by title
npm run test:e2e:flow -- --headed          # watch the browser
npm run test:e2e:flow -- --ui              # Playwright UI mode
```

## Directory

```
tests/e2e-flow/
├── README.md                            (this file)
├── fixtures/
│   ├── tenant.ts                        worker-scoped tenant-id factory
│   ├── auth.ts                          Firebase emulator sign-up/sign-in
│   └── stripe.ts                        Stripe test-mode trigger helpers
├── probes/
│   ├── db-probe.ts                      read-only DB assertions (via admin test-probe endpoint)
│   └── bus-probe.ts                     NATS JetStream ephemeral consumer for event assertions
└── workflows/
    ├── subscription-happy-path.spec.ts  TASK-E2E-001 (working spike)
    ├── subscription-declined.spec.ts    TASK-E2E-002 (skeleton)
    └── subscription-cancel-back.spec.ts TASK-E2E-003 (skeleton)
```

## Isolation — how it actually works

Every spec file uses the `e2eTest` fixture (from `fixtures/tenant.ts`) instead of bare `test`:

```ts
import { e2eTest as test, expect } from '../fixtures/tenant'

test('subscription happy path', async ({ page, tenant, authUser, stripeScope }) => {
  // tenant.id    = 't_e2e_<workerIndex>_<runId>'
  // authUser     = fresh Firebase user with session cookie set on page
  // stripeScope  = scoped Stripe helpers bound to this tenant's metadata
})
```

The `tenant` fixture is worker-scoped (once per worker process, shared across specs on that worker). `authUser` and `stripeScope` are test-scoped (fresh per spec). This gives you:

- Fast: same tenant for all specs on worker N → no constant setup churn
- Isolated: different workers → different tenants → zero cross-talk
- Clean: each spec creates its own Firebase user → no stale session pollution

## Why no DB truncate between tests?

Tenant scoping makes it unnecessary. Every write is tagged with `tenant_id`; every read filters by it. Tests that genuinely need a blank slate can grab a fresh tenant via `tenant.fork()` inside the spec — cheap (one row) compared to full-DB truncate (seconds on Marten).

## Debugging a failure

1. `npm run test:e2e:flow -- --grep "<spec title>" --headed` reproduces with visible browser
2. Open `test-results/e2e-flow/artifacts/<spec>/trace.zip` in `npx playwright show-trace` for the replay
3. Check `test-results/e2e-flow/report/index.html` for the structured rundown
4. If the failure is tenant-specific, print `tenant.id` in the spec and query DB manually via `/api/admin/test/probe?tenantId=X`

## Related

- `tasks/e2e-flow/` — the task-level plans (architecture, multi-agent runner)
- `tests/e2e/` — UI-only specs (separate discipline)
- `tests/unit/` — vitest unit tests
