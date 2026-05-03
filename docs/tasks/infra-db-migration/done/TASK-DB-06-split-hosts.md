# TASK-DB-06: Split Hosts — `Cena.Student.Api.Host` + `Cena.Admin.Api.Host`

**Priority**: HIGH
**Effort**: 4-6 days
**Depends on**: DB-03 (AutoCreate.None) and DB-05 (contracts extracted)
**Track**: C
**Status**: Not Started

---

## You Are

A senior platform architect executing the host split you designed. You isolate HTTP edges without splitting the domain. You keep Marten, NATS, actors, and contracts shared — everything else is per-host. You ship with the deploy pipeline and observability story intact on day one.

## The Problem

[src/api/Cena.Api.Host/Program.cs](../../../src/api/Cena.Api.Host/Program.cs) maps both student endpoints (`MapSessionEndpoints`, `MapStudentAnalyticsEndpoints`, `MapComplianceEndpoints`) and admin endpoints (`MapCenaAdminEndpoints`) in the same process. This:

- **Shared blast radius**: an admin bug (e.g. a bad ingestion path) can crash the process that's running live student sessions.
- **Shared scaling**: admin is tiny, student is bursty — they're scaled together because they're one pod.
- **Shared deploy cadence**: shipping an admin hotfix reboots live student sessions.
- **Shared auth posture**: student is public-internet (Firebase JWT per user), admin wants RBAC + MFA + VPN — one CORS/rate-limit config can't serve both well.
- **Shared SLOs**: student p99 < 200ms vs admin tolerating seconds — one set of resource limits can't satisfy both.

## Your Task

Split the host process into two while keeping the domain shared.

### 1. New layout

```
src/api/
├── Cena.Api.Contracts/          (from DB-05)
├── Cena.Db.Migrator/            (from DB-02)
├── Cena.Student.Api.Host/       ← NEW
│   ├── Program.cs               (maps /api/sessions, /api/content, /api/analytics, /hub/cena)
│   ├── Cena.Student.Api.Host.csproj
│   ├── appsettings.json
│   ├── Dockerfile
│   └── Properties/
├── Cena.Admin.Api.Host/         ← NEW
│   ├── Program.cs               (maps /api/admin/*, live monitor, ingestion endpoints)
│   ├── Cena.Admin.Api.Host.csproj
│   ├── appsettings.json
│   ├── Dockerfile
│   └── Properties/
├── Cena.Admin.Api/              (existing endpoint library — stays, now consumed by admin host only)
└── Cena.Api.Host/               ← DELETED after cut-over
```

### 2. Student host responsibilities

Maps exactly what a student-facing client (mobile + web) needs:

- `GET /api/sessions/*` (existing)
- `GET /api/content/*` (existing)
- `GET /api/analytics/*` (existing)
- `POST /api/sessions/start` (to be added, see [docs/student/15-backend-integration.md](../../student/15-backend-integration.md))
- All new `/api/me/*`, `/api/gamification/*`, `/api/tutor/*`, `/api/challenges/*`, `/api/social/*`, `/api/notifications/*` endpoints from the student docs
- `/hub/cena` SignalR hub

Config:
- Firebase JWT auth (existing middleware)
- CORS: `https://app.cena.education`, `https://dev-app.cena.education`, localhost for dev
- Rate limits: `api` policy = 120 req/min per user, `hub` = 60 msg/min per connection (already in use)
- Health endpoints: `/health/live`, `/health/ready`
- `AutoCreate.None` (from DB-03)
- `AssertDatabaseMatchesConfigurationAsync()` at startup

### 3. Admin host responsibilities

Maps the admin surface that's currently in `Cena.Admin.Api`:

- `/api/admin/*` (all admin endpoints)
- Live monitor SignalR hub (if separate)
- Ingestion + moderation flows
- GDPR + compliance endpoints

Config:
- Firebase JWT + RBAC (admin roles required)
- MFA check if configured
- CORS: admin UI origins only
- Rate limits: `admin` policy (looser, fewer users)
- Health endpoints
- `AutoCreate.None` (from DB-03)
- `AssertDatabaseMatchesConfigurationAsync()` at startup

### 4. Shared between hosts

- `Cena.Api.Contracts` — DTOs, hub contracts, envelope
- `Cena.Actors` — actors, events, Marten schema config, upcasters
- `Cena.Infrastructure` — auth middleware, rate limiting, observability, data source factory
- Postgres / Marten — one DB, one stream, one schema
- NATS — same subjects, same actors

### 5. Deprecate `Cena.Api.Host`

Once both new hosts are running cleanly in staging:

1. Mark `Cena.Api.Host.csproj` with a `NotDeployed` property or similar.
2. Leave the project in the repo for one release cycle as a safety net.
3. Delete it in a follow-up PR after two clean prod deploys.

### 6. Wiring checklist

- `Cena.sln` — add both new host projects.
- Both new hosts reference `Cena.Api.Contracts`, `Cena.Actors`, `Cena.Infrastructure`, and (admin only) `Cena.Admin.Api`.
- Dockerfiles: multi-stage build, non-root user, distinct image names (`cena/student-api`, `cena/admin-api`).
- Environment variables: both read from the same `ConnectionStrings__Cena`, `Firebase__*`, `Nats__*`.
- Observability: both hosts emit correlation IDs; distributed tracing via OpenTelemetry spans both processes.
- Logs: include `host=student|admin` field.

### 7. Integration test migration

Existing tests under `src/api/Cena.Admin.Api.Tests/` must continue to pass. If any test uses a `Cena.Api.Host` factory, retarget it at whichever new host owns the endpoint being tested.

## Files You Must Create

- `src/api/Cena.Student.Api.Host/Program.cs`
- `src/api/Cena.Student.Api.Host/Cena.Student.Api.Host.csproj`
- `src/api/Cena.Student.Api.Host/appsettings.json`
- `src/api/Cena.Student.Api.Host/appsettings.Development.json`
- `src/api/Cena.Student.Api.Host/Dockerfile`
- `src/api/Cena.Admin.Api.Host/Program.cs`
- `src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj`
- `src/api/Cena.Admin.Api.Host/appsettings.json`
- `src/api/Cena.Admin.Api.Host/appsettings.Development.json`
- `src/api/Cena.Admin.Api.Host/Dockerfile`

## Files You Must Modify

- `Cena.sln`
- Any integration test project that references `Cena.Api.Host`
- `docs/student/00-overview.md` and `docs/student/15-backend-integration.md` to reflect the split

## Files You Must Read First

- [src/api/Cena.Api.Host/Program.cs](../../../src/api/Cena.Api.Host/Program.cs) — the full current wiring
- [src/api/Cena.Api.Host/Endpoints/](../../../src/api/Cena.Api.Host/Endpoints/) — the endpoints the student host will own
- [src/api/Cena.Admin.Api/](../../../src/api/Cena.Admin.Api/) — what the admin host will own
- `docs/student/00-overview.md` — the planned deployment architecture

## Acceptance Criteria

- [ ] Two new host projects exist and build independently.
- [ ] `Cena.Student.Api.Host` serves all student-facing REST and SignalR endpoints.
- [ ] `Cena.Admin.Api.Host` serves all admin REST endpoints.
- [ ] `Cena.Api.Host` is no longer the target of deploys (still in the repo for safety, marked `NotDeployed`).
- [ ] Both hosts start with `AutoCreate.None` and pass `AssertDatabaseMatchesConfigurationAsync()`.
- [ ] Both hosts reference the same `Cena.Api.Contracts` and `Cena.Actors`.
- [ ] Distinct Docker images built for each host.
- [ ] CORS, rate limits, and auth policies are configured independently per host.
- [ ] Existing integration tests still pass (retargeted where needed).
- [ ] `docs/student/00-overview.md` reflects the split in the architecture diagram and stack section.
- [ ] `docs/student/15-backend-integration.md` lists the new base URLs and clarifies which host owns which endpoints.
- [ ] Staging deploy has been verified: both hosts up, student endpoints reachable, admin endpoints reachable, schema clean.
- [ ] Rollback recipe documented in the PR description.

## Out of Scope

- Actually deleting `Cena.Api.Host` — follow-up after two clean prod deploys.
- Deploy ordering automation — that's DB-07.
- Adding new student endpoints (`POST /api/sessions/start`, `/api/me/*`, etc.) — those are tracked in the `STU-*` task IDs under `docs/student/`.
