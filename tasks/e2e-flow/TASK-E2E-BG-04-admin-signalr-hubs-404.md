# TASK-E2E-BG-04: Backend gap — 4 SignalR hubs return 404 on negotiate

**Status**: Proposed
**Priority**: P1
**Epic**: Backend gap fixes (EPIC-E2E-G + EPIC-E2E-J resilience)
**Tag**: `@backend-gap @admin-api @signalr @p1`
**Owner**: admin-api maintainers
**Surfaced by**: EPIC-G admin smoke matrix — 4 admin pages negotiate WebSocket connections that 404

## Evidence

Each of these admin pages opens a SignalR connection on mount:

| Page | Hub URL | Negotiate result |
| --- | --- | --- |
| `/apps/sessions/monitor` | `/sessionMonitor` | 404 Not Found |
| `/apps/system/actors` | `/actors` | 404 Not Found |
| `/apps/system/architecture` | `/architecture` | 404 Not Found |
| `/apps/system/events` | `/events` | 404 Not Found |

Console error pattern:

```
[2026-04-27T15:18:27.695Z] Error: Failed to complete negotiation with the server:
  Error: Not Found: Status code '404' Either this is not a SignalR endpoint or
  there is a proxy blocking the connection.
```

Each page falls back to a static "no live data" empty state, so the user UX is degraded but not broken — but the page console shouts on every mount, drowning out real signal.

## What to investigate

1. Verify the hub route mappings in `cena-admin-api` `Program.cs`. Search for `MapHub<T>` calls.
2. Compare to the SPA's `useSignalRConnection` composable — what URL is each page using?
3. Hubs that DO work today (for contrast): the cena-actor-host exposes a `/sessions` hub that EPIC-E2E-J's resilience tests rely on. Look at how that hub is mapped and mirror.
4. Possible causes:
   - The 4 hubs are partially-implemented surfaces from an earlier sprint and never got their `MapHub` registration
   - Vite proxy mis-routes `/sessionMonitor` (no `/api/` prefix — most other endpoints proxy via the `/api` rule in vite.config.ts)
   - The hubs are mapped on the **actor host**, not the admin-api, but the SPA points them at the admin-api gateway

## Definition of done

- [ ] Each of the 4 hubs is either:
  - Mapped + a real broadcaster wired up (preferred), OR
  - Removed from the SPA mount path with a feature-flag stub that doesn't fire SignalR negotiation when the flag is off
- [ ] The 4 EPIC-G admin smoke allowlist entries are REMOVED
- [ ] Per-hub regression test: `EPIC-G-system.spec.ts` (TASK-E2E-COV-04 sub-spec) drives each page and asserts no console-error from the hub
- [ ] If hubs are kept, document their event contract in `docs/api/signalr-hubs.md` — which events are pushed, what payload shape, what the SPA does with each

## Why this matters

EPIC-E2E-J resilience scenarios depend on at least one of these hubs (`/apps/system/events` is supposed to live-tail event-stream activity per RDY-060 Phase 5e). Without the hubs working, J-09 (SignalR reconnect) test is deferred indefinitely.
