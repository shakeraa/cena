# REPORT-EPIC-J — Resilience / failure modes (real-browser journey)

**Status**: ✅ green for the SymPy-sidecar chaos cycle (J-01-equivalent). Other peripherals queued — see gaps below.
**Date**: 2026-04-27
**Worker**: claude-1
**Spec file**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-J-resilience-journey.spec.ts`

## What this spec exercises

Container-chaos drive: stop the SymPy CAS sidecar mid-session, verify the SPA does not litter the chrome console with uncaught exceptions, then restart the sidecar and verify the SPA recovers cleanly on reload.

Steps:
1. Provision a fresh student (Firebase emu signUp + on-first-sign-in)
2. Drive `/login` form, persist Firebase IndexedDB session
3. `stopService('cena-sympy-sidecar')` via the chaos probe
4. Navigate to `/home` while the sidecar is down
5. Snapshot console errors + page errors observed during the outage
6. `startService('cena-sympy-sidecar')`
7. Hard reload — re-hydrate, no errors

The key assertion: **zero uncaught JS exceptions throughout the chaos cycle**. The SPA may surface a user-facing error (toast, error card) — that's fine. What it must not do is throw uncaught while a peripheral is down.

`afterEach` always restarts the sidecar so a mid-test failure doesn't poison the rest of the suite.

## Why SymPy sidecar (not NATS / Firebase / Postgres)

The chaos probe (`tests/e2e-flow/probes/chaos.ts`) supports all those services, but:

- **NATS down** would cascade into `cena-actor-host` going unhealthy, which breaks subsequent specs in the same run.
- **Firebase emu down** breaks the entire signed-in journey; the test can't even reach `/home`.
- **Postgres down** is too destructive for the dev container co-tenancy.

SymPy is the **lowest-blast-radius** chaos service. Stopping it produces a clean signal: any flow that doesn't depend on CAS keeps working, any flow that does should degrade gracefully.

## Buttons / endpoints

- `/login`: real form drive
- `/home`: nav-only (the negative signal is JS-error during nav)
- `chaos.stopService` / `chaos.startService` (compose CLI shell-out)

## Gaps surfaced (queue these as separate specs)

- **J-02 LLM 429 circuit breaker**: needs the LLM recorder probe in a 429 mode; doable but requires new probe surface
- **J-03 NATS down → outbox accumulates → drains on restart**: requires asserting outbox state via PRR-436 admin probe
- **J-04 Firebase emu down**: existing-session-keeps-working assertion — separate spec because it needs a different setup ordering
- **J-05 Stripe webhook burst idempotency**: server-side test
- **J-06 SMTP down**: needs SMTP probe (Mailpit?)
- **J-07 Redis down → rate limiter fail-closed**: requires asserting 429 response shape
- **J-08 per-student rate limit**: existing in pp-005 worktree
- **J-09 SignalR reconnect**: needs SignalR probe + WS chaos

This spec covers the J-01 spirit (CAS-sidecar-down) end-to-end. The rest stay queued — they're different shapes of test, not just clones of this one.

## Diagnostics

Per-test JSON attachments:
- `console-entries.json` (full console trail)
- `page-errors.json` (uncaught exceptions — must be empty)
- `failed-requests.json` (4xx/5xx)
- `chaos-snapshot.json` (counts during the chaos window specifically)

## Build gate

Full suite: 39 passed / 1 fixme.

## What's next

J-04 Firebase-down is the next-most-urgent because it tests the SPA's "session survives auth-emu hiccup" claim — a real production scenario. Queue separately.
