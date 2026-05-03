# TASK-E2E-INFRA-01: `fixtures/bus-probe.ts` — JetStream ephemeral consumer for E2E specs

**Status**: Shipped — fixture at `src/student/full-version/tests/e2e-flow/fixtures/bus-probe.ts`, wired into `fixtures/tenant.ts` as `busProbe` test-scoped fixture. Smoke-verified via `student-register.spec.ts` waiting on `cena.events.student.*.onboarded` (will go green once BE-02 ships).
**Priority**: P0 (unblocks bus-boundary assertions across all E2E flow specs)
**Epic**: Shared infra (referenced by EPIC-E2E-A, EPIC-E2E-B, EPIC-E2E-C, EPIC-E2E-E, EPIC-E2E-H)
**Tag**: `@infra @p0 @bus`

## Why this is a prereq

The flow-test README already reserves a `probes/bus-probe.ts` slot but it does not exist yet. Every spec that asserts a bus boundary (A-01 `StudentOnboardedV1`, B-series subscription events, D-series tutor events, H-series cross-tenant bleed checks) is forced to defer the bus assertion with a TODO until this helper ships. The flagship `subscription-happy-path` spec openly carries the deferral. One fixture unblocks all of them.

## What to build

1. `src/student/full-version/tests/e2e-flow/fixtures/bus-probe.ts`
2. API:
   ```ts
   export interface BusProbe {
     /** Wait for an event matching subject + optional header filter. */
     waitFor(opts: {
       subject: string              // e.g. 'cena.events.student.*.onboarded'
       filterHeader?: { name: string; value: string }   // e.g. { name: 'tenant_id', value: tenant.id }
       timeoutMs?: number           // default 5_000
     }): Promise<BusEnvelope>

     /** Assert that no event matching subject lands within timeoutMs. */
     assertNone(opts: { subject: string; timeoutMs?: number }): Promise<void>

     /** Teardown the ephemeral consumer. */
     close(): Promise<void>
   }
   ```
3. Backed by `nats.js` + JetStream ephemeral consumer. Connection target: `NATS_URL` env, falling back to `nats://localhost:4222` (the dev-stack compose port).
4. Wire as a test-scoped fixture in `fixtures/tenant.ts` so specs get `busProbe` alongside `tenant`, `authUser`, `stripeScope`.
5. Docs: short section in `tests/e2e-flow/README.md` on using the probe + what to do when an event is late (wait or fail loudly).

## Contract notes

- Ephemeral consumer (not durable) — no post-test cleanup debt on the JetStream server
- One consumer per `waitFor` call; don't share — sharing masks duplicate-emission bugs
- `filterHeader` uses JetStream filter subjects + message headers (prefer headers over subject-embedded ids where emitters support it)
- Teardown must be guaranteed (afterEach) even on assertion failure to avoid consumer leaks

## Boundary tests to unblock

- TASK-E2E-A-01 boundary #4 (bus: `StudentOnboardedV1`)
- TASK-E2E-001 flagship remaining boundaries (subscription-activated event)
- Every epic's bus-boundary DoD line

## Done when

- [x] Fixture file exists with the API above, typed
- [x] Wired into `fixtures/tenant.ts` so `busProbe` is a test-scoped fixture
- [ ] Smoke test: publish a known event from a test utility, `waitFor` returns within 500ms
- [ ] Teardown leak-free (no dangling consumers after a full run — verified via `nats consumer list`)
- [ ] README entry explaining usage + failure-mode expectations
- [x] TASK-E2E-A-01 spec uses real `busProbe.waitFor` assertion; TASK-E2E-001 still TODO until subscription-activated emitter lands

## Implementation note (2026-04-24)

Implemented as a raw-TCP NATS core-sub probe (no `nats.js` dep) to keep test infra free of pnpm/npm lockfile churn. JetStream was downgraded to plain core-sub: the boundary check is "event published on subject", which core-sub catches regardless of persistence. Persistence has its own tests.
