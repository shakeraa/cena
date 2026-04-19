# RDY-060: Admin SignalR — Replace Poll-Based Dashboards

- **Status**: Requested — not started
- **Priority**: Medium-High (admin UX + server load)
- **Source**: Shaker question 2026-04-18: "why auto-refresh instead of SignalR?"
- **Answer**: we never wired SignalR admin-side. Polling is the lazy fallback.
- **Tier**: 2 (quality signal, not a ship-blocker)
- **Effort**: 3-5 days end-to-end
- **Depends on**: RDY-020 (SignalR event push-back — landed, student-side).

## Problem

Every admin dashboard polls admin-api on an interval:

| Page | Interval | File |
|---|---|---|
| Live Monitor | 3s | `src/admin/full-version/src/pages/apps/sessions/monitor.vue` |
| System Architecture | 5s | `src/admin/full-version/src/pages/apps/system/architecture.vue` |
| Actor Dashboard | 3s | `src/admin/full-version/src/pages/apps/actor/dashboard.vue` (implied) |
| Dead Letters | on-mount + manual refresh | `dead-letters.vue` |
| Event Stream | on-mount + manual refresh | `event-stream.vue` |
| Student Profile Insights | on-mount (timeline / heatmap / degradation / engagement / error-types / hint-usage / stagnation / response-times) | `UserTabInsights.vue` |

Costs:
- ≈1200 requests/hour per open admin tab.
- 0-3 s latency before a teacher sees a signal ("student just got confused"
  shows up 3 seconds late, worst-case 3000 ms on a slow link).
- Every open tab duplicates work — no fan-out caching because each tab
  has its own JWT and query params.
- Admin-api CPU time mostly spent serving near-identical payloads.

The student side already solved the real-time problem: `CenaHub` +
`NatsSignalRBridge` (under `src/api/Cena.Student.Api.Host/Hubs/`) subscribes
to `cena.events.student.{uid}.*` on NATS and pushes deltas into a
per-student SignalR group. One WebSocket, zero polling, sub-500 ms latency.

Admin dashboards deserve the same pattern — tenant-scoped instead of
per-student-scoped.

## Scope

### 1. Port the student-side Hubs pattern to admin-api

Copy the four files under `Cena.Student.Api.Host/Hubs/` into
`Cena.Admin.Api.Host/Hubs/`, with three intentional changes:

- **CenaAdminHub** — Hub<ICenaAdminClient>. Exposes methods:
  - `JoinSchool(schoolId)` / `LeaveSchool(schoolId)`
  - `JoinSystemMonitor()` / `LeaveSystemMonitor()`
  - `JoinStudentInsights(studentId)` / `LeaveStudentInsights(studentId)`
  - `JoinIngestionPipeline()` / `LeaveIngestionPipeline()`
  Every Join verifies the caller's claim covers the requested scope (school
  id must match their tenant; SuperAdmin bypasses). Audit-log every join.

- **NatsAdminBridge** — subscribes to a broader subject set:
  - `cena.events.session.>` (tutoring sessions)
  - `cena.events.focus.>` (focus rollups)
  - `cena.events.ingestion.>` (pipeline state)
  - `cena.system.>` (actor host lifecycle, circuit breakers, DLQ)
  Parses subject → determines target SignalR group → pushes the event
  payload as-is (no re-shaping — clients handle schema).

- **AdminGroupManager** — same idempotent-join pattern as the student side,
  but tracks per-connection groups so a `LeaveSchool` hit cleans the
  connection's subscription.

### 2. Auth / handshake

- JWT via `access_token` query string (the student-side pattern at
  `SignalRConfiguration.cs:47`). Firebase admin JWT only — the existing
  `AddFirebaseAuth` pipeline validates it.
- `Cors__AllowedOrigins__0/1` (dev emulator + localhost) already allow
  credentials; verify the SignalR handshake request passes CORS.

### 3. Redis backplane for horizontal scaling

Admin-api can run behind multiple replicas (K8s provider is wired per
RDY-025b). Redis backplane is required so a NATS event arriving on
replica A gets fanned out to clients connected to replica B.

- Add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` package.
- Register `.AddSignalR().AddStackExchangeRedis(...)` with the existing
  `REDIS_PASSWORD` connection string.
- Acceptance: a test spinning two admin-api replicas + one client on each,
  firing a NATS event, verifies both clients receive it.

### 4. Client composable: `useAdminLiveStream`

New file: `src/admin/full-version/src/composables/useAdminLiveStream.ts`.

```ts
const { events, status, joined, join, leave } = useAdminLiveStream()

await join('school:school-haifa-01')   // claim-scoped
await join('system')                    // SuperAdmin only
```

- Manages a singleton `HubConnectionBuilder().withAutomaticReconnect()`
  using the same Firebase token the rest of the SPA uses.
- Exposes a typed event stream via VueUse `useEventBus` (or plain ref
  of arrays) so page components can subscribe without each page opening
  its own connection.
- On route change, emits a signal so pages can cleanly unsubscribe.

### 5. Migrate the five identified pages

Phase-migrate, one page per PR:

- **Phase 5a — Live Monitor** (`sessions/monitor.vue`):
  drop the `setInterval`, subscribe to `session.*.turn` /
  `session.*.status-changed` events, mutate local state on each event.
  Initial fetch on mount stays (to hydrate the grid), stream takes
  over from there.
- **Phase 5b — System Architecture** (`system/architecture.vue`):
  `cena.system.*` → update node statuses live. Keep the 30s fallback
  poll as a safety net.
- **Phase 5c — Actor Dashboard** (`actor/dashboard.vue`): same as
  System Architecture but with per-actor stats.
- **Phase 5d — Student Profile Insights**: 8 panels, each subscribes
  to `cena.events.focus.{studentId}.*` or
  `cena.events.session.{sessionId}.*` as appropriate.
- **Phase 5e — Event Stream + Dead Letters**: replace the "refresh"
  button with a live-tail that prepends rows as events arrive;
  pause/resume control.

### 6. Graceful degradation

If the WebSocket fails to connect (CORS, token expiry, proxy strips
Upgrade headers), fall back to the existing `setInterval` poller and
log a warning. Don't regress the page to offline; just run slower.

### 7. Observability

- Counter: `cena_admin_signalr_connections` (gauge, current connected
  admins)
- Counter: `cena_admin_signalr_events_sent_total` by subject
- Histogram: `cena_admin_signalr_group_join_duration_seconds`
- Log line on every join/leave for the admin audit stream.

## Acceptance Criteria

- [ ] `CenaAdminHub` + `NatsAdminBridge` + `AdminGroupManager` land in
  admin-api. Unit tests cover: claim-scoped group membership, reject on
  cross-tenant join, idempotent re-join.
- [ ] Redis backplane configured. Two-replica integration test passes.
- [ ] `useAdminLiveStream` composable documented; single shared
  HubConnection per SPA session.
- [ ] All five pages migrated. `setInterval`-based polling removed from
  `monitor.vue`, `architecture.vue`, `actor/dashboard.vue`, and the
  UserTabInsights panels. Each retains a 30s safety poll in case the
  stream is down.
- [ ] Architecture test (extends `AdminSpaPagesHaveNoPlaceholdersTest`
  pattern) flags new .vue files that declare `setInterval` without an
  allowlisted comment, so future dashboards don't regress.
- [ ] Metrics exported and visible on the observability dashboard.
- [ ] Admin API server CPU on an idle dashboard (5 tabs open, no
  student traffic) drops by ≥50 % vs the polling baseline.

## Out of scope

- Replacing the student-side SignalR stack (works fine).
- Admin-initiated push to students (different direction; not covered).
- Offline-first sync for admin dashboards (would need service-worker +
  IndexedDB — separate task if a PM ever asks).

## Notes

- Reuse the existing NATS subjects — don't mint new admin-specific
  topics unless a delivery signal is genuinely admin-only (e.g. ingestion
  operator notifications). Keep the event bus canonical.
- Tenant-scoping on groups is load-bearing for privacy: a SuperAdmin in
  one tenant must NEVER see a tutoring turn from another tenant. Test
  this explicitly — `AdminGroupManager` should reject
  `JoinSchool("other-tenant")` and log an IDOR violation.
- The 30s fallback poll is not a stub; it's belt-and-suspenders against
  silent WebSocket failure (proxy timeouts, network blips). Keeps the
  dashboard eventually-consistent even in degraded mode.

## Links

- Student-side reference implementation:
  - [src/api/Cena.Student.Api.Host/Hubs/CenaHub.cs](../../src/api/Cena.Student.Api.Host/Hubs/CenaHub.cs)
  - [src/api/Cena.Student.Api.Host/Hubs/NatsSignalRBridge.cs](../../src/api/Cena.Student.Api.Host/Hubs/NatsSignalRBridge.cs)
  - [src/api/Cena.Student.Api.Host/Hubs/SignalRConfiguration.cs](../../src/api/Cena.Student.Api.Host/Hubs/SignalRConfiguration.cs)
  - [src/api/Cena.Student.Api.Host/Hubs/SignalRGroupManager.cs](../../src/api/Cena.Student.Api.Host/Hubs/SignalRGroupManager.cs)
- Commit `1134749` — honest-fields Live Monitor that exposed the 3s poll.
- RDY-020 parent: SignalR event push-back.
