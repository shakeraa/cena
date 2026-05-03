# TASK-STU-W-03: `$api` Wrapper, SignalR Client, Typed Hub Events

**Priority**: HIGH — blocks every feature task that talks to the backend
**Effort**: 2-3 days
**Phase**: 1
**Depends on**: [STU-W-01](TASK-STU-W-01-design-system-bootstrap.md), [DB-05](../../docs/tasks/infra-db-migration/TASK-DB-05-contracts-library.md)
**Backend tasks**: none (uses existing endpoints + the new contracts library from DB-05)
**Status**: Not Started

---

## Goal

Centralize every network call and realtime subscription behind typed composables so feature tasks can consume them without knowing about tokens, retries, envelopes, or reconnection logic.

## Spec

Full specification lives in [docs/student/15-backend-integration.md](../../docs/student/15-backend-integration.md). All `STU-API-*` acceptance criteria in that file form this task's checklist.

## Scope

In scope:

- `src/api/$api.ts` — `ofetch` wrapper with:
  - Firebase ID token attached to every request
  - 401 → token refresh → retry once → redirect to `/login` on second failure
  - 429 → exponential backoff with jitter, max 3 retries, toast on final failure
  - 502/503/504 → exponential backoff, max 3 retries
  - Correlation ID (`X-Correlation-Id`) generated per request and logged
  - Response type inference from endpoint signatures
- `src/api/signalr.ts` — SignalR client wrapper with:
  - Auto-reconnect with exponential backoff `[0, 2000, 10000, 30000]`
  - Access token factory that reads the current Firebase ID token
  - `subscribe<T>(eventName)` returning a reactive Vue `Ref<T | null>`
  - `send<T>(eventName, payload)` with in-flight queue that replays on reconnect
  - Connection state exposed as a reactive `Ref<'connected' | 'reconnecting' | 'disconnected'>`
- `src/api/types/` — TypeScript types generated from `Cena.Api.Contracts` via the codegen stub added in DB-05. This task implements the actual codegen script that runs in CI and checks in the generated types.
- `src/api/hub-events.ts` — typed enum + helpers for all `BusEnvelope<T>` events from `HubContracts.cs`
- `src/composables/useApi.ts` — convenience composables:
  - `useApiQuery(path, options)` — GET with cache + loading/error state
  - `useApiMutation(path, method)` — POST/PUT/PATCH/DELETE with optimistic UI helpers
- `src/composables/useDraftAutosave.ts` — saves a `Ref<string>` to `localStorage` every 5 s with a `saved <time ago>` indicator
- `src/composables/useOptimistic.ts` — optimistic state + rollback on error
- Sentry wired for errors and performance (LCP, CLS, FID)
- `BusEnvelope` unwrap helper — reactive hook that strips the envelope and exposes only the payload

Out of scope:

- Any page-specific API calls (feature tasks)
- Service worker / offline support (STU-W-15)
- Actual typed responses for endpoints that don't exist yet (stub with `TODO` markers and add when backend ships)

## Definition of Done

- [ ] All `STU-API-001` through `STU-API-015` acceptance criteria from [15-backend-integration.md](../../docs/student/15-backend-integration.md) pass
- [ ] `$api.get('/api/analytics/summary')` works against a running dev host and returns typed data
- [ ] SignalR connects to `/hub/cena`, receives `XpAwarded` test event, and a Pinia store observes it
- [ ] Disconnecting the network shows the `reconnecting` state; reconnecting replays the queued events
- [ ] Token expiry triggers a refresh and the request retries transparently
- [ ] 429 response shows a toast and backs off; 401 without refresh redirects to `/login`
- [ ] CI job regenerates TypeScript types from `Cena.Api.Contracts` and fails if the checked-in types are stale
- [ ] Draft autosave composable saves to localStorage and shows "saved 5s ago"
- [ ] Sentry receives a test error from the frontend
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Codegen loop** — generating TS from .NET types is notoriously fiddly. Prefer a simple reflection-based tool (e.g. TypeGen, NSwag, or a hand-rolled Roslyn script) over anything that requires a running backend. Keep it deterministic.
- **Firebase token refresh jitter** — the SDK refreshes tokens proactively; avoid double-refreshing in the 401 retry path.
- **Correlation ID propagation** — must be attached to Sentry events so frontend errors can be cross-referenced with backend traces.
- **SignalR reconnect storms** — if many clients reconnect at once after a backend deploy, exponential backoff with jitter is essential.
