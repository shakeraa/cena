# TASK-E2E-G-09: Live session monitor SSE (ADM-026)

**Status**: Proposed
**Priority**: P2
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @sse @p2`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/live-monitor.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Admin opens `/apps/system/live-monitor` → SSE stream shows active sessions across institutes → admin drills into a session (read-only) → stream reconnects on backend restart.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | List updates live |
| SSE | Reconnection behavior |
| PII | No data beyond admin entitlement |

## Regression this catches

Stream frozen silently; reconnection fails; PII leak beyond admin role.

## Done when

- [ ] Spec lands
