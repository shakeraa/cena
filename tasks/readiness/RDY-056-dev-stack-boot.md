# RDY-056: Dockerized Dev Stack — Bootable End-to-End

- **Priority**: High (unblocks local product testing and UAT)
- **Complexity**: Senior engineer, ~1 full day focused
- **Source**: Session 2026-04-17 — SymPy sidecar + compose overlay shipped, .NET hosts crashed at boot on DI + Marten schema races
- **Tier**: 3 (polish / DX; not a ship-blocker)
- **Effort**: 6-10 focused hours (three phases)
- **Dependencies**: none new — builds on the SymPy sidecar + `docker-compose.app.yml` already landed

## Problem

On 2026-04-17 we stood up the full compose stack. Infra + SymPy sidecar + DB migrator booted cleanly, but the three .NET services (`admin-api`, `student-api`, `actor-host`) crashed on first boot for three distinct reasons. CPU on each container spiked to ~100% because `restart: unless-stopped` put them in a crash loop; the root causes are:

### Failure 1 — Admin API: Marten schema-creation deadlock

`CulturalContextSeeder` (`IHostedService`) runs at startup and issues a Marten query before Marten's schema-ensure machinery has completed. Weasel's `TimedLock` times out, throwing `TaskCanceledException`. Host crashes, restart, same race, loop.

**Evidence**: `CulturalContextSeeder.StartAsync` → `Marten.Linq.MartenLinqQueryProvider.ExecuteAsync` → `Weasel.Core.Migrations.TimedLock.Lock` timeout.

### Failure 2 — Student API: unregistered services (DI validation)

`ValidateOnBuild = true` is still tripping despite the `IsDevelopment()` gate we added to `Program.cs`. Either a hosted-service construction path runs before our override takes effect, or the student API has a second `AddAuthentication` / validation hook that still enforces eager resolution. Stack ends at `Host.StartAsync`, not a service-provider build — which suggests a hosted service construction failure, not DI validation.

### Failure 3 — Actor Host: DI validation error

Same family as #2, but at the `ServiceProvider..ctor` / `ValidateService` stage. Actor Host doesn't set `UseDefaultServiceProvider` with our Development override. Hosted services expect services that are registered in Admin or Student host but not Actor Host.

### Cross-cutting: Firebase auth in local dev

Even if all three services boot, login is unreachable locally. `FirebaseAuthExtensions.AddFirebaseAuth` hard-codes `https://securetoken.google.com/{projectId}` as both authority and issuer. Grep across `src/` for `FIREBASE_AUTH_EMULATOR_HOST` returns zero hits — the emulator has never been wired. Without this, no user can log in to either SPA locally.

## Scope — three sequenced phases

### Phase 1 — Make the three .NET containers boot (3-5 hours)

**1.1 Marten schema readiness gate**

- Add an `IHostedService` called `MartenSchemaReadyCheck` that runs **first** (registered before any seeder) and blocks until `DocumentStore.Advanced.Clean.CompletelyRemoveAllAsync` → `EnsureSchemaObjectsExistAsync` succeed or a 60s budget expires.
- Make every seeder (`CulturalContextSeeder`, `QuestionBankSeedData`, `FirebaseClaimsSeeder`, etc.) depend on this check. Options:
  - (a) Register seeders with `AddHostedService` **after** the schema check so the host orders them correctly, OR
  - (b) Inject an `IMartenSchemaGate` that seeders `await` before querying.
- Acceptance: admin-api boot log shows `[MARTEN_SCHEMA_READY]` before any seeder log line; no more `TaskCanceledException` from `TimedLock`.

**1.2 Uniformly disable `ValidateOnBuild` in Development**

- Current: only `admin-api` + `student-api` `Program.cs` have the Development gate. Actor host doesn't.
- Fix: extract a `CenaHostDefaults.UseCenaDevServiceProvider(IHostBuilder, IHostEnvironment)` into `Cena.Infrastructure`. Call from all three `Program.cs`. Remove duplicate fragments.
- Acceptance: all three hosts honour `ValidateOnBuild = false` when `ASPNETCORE_ENVIRONMENT=Development`; DI errors surface on first request, not on startup.

**1.3 Register the shared service set in every host that needs it**

Services that are currently host-specific but needed by shared registrations:

- `Cena.Actors.RateLimit.ICostBudgetService` / `ICostCircuitBreaker` — used by `RateLimitAdminService` (shared Admin code) + `RateLimitDashboardEndpoints` (Student host only). Admin host now registers both (this session). Verify Actor host registers both too.
- `Cena.Infrastructure.Compliance.IClock` / `IErasureCryptoConfig` / `IErasureManifestBuilder` — used by `RightToErasureService` (shared Admin code). Admin host now registers all three (this session). Verify Student + Actor hosts either don't need them or register them.
- Collect every `InvalidOperationException: Unable to resolve service for type ...` seen across the three hosts, register each at the host that owns `AddCenaAdminServices()` / `AddCenaStudentServices()` / actor-host registrations.

Acceptance: container boot logs reach `Now listening on: http://+:5050` (or 5052) on all three, no `InvalidOperationException` before.

**1.4 Health-probe dependency graph**

- Currently `admin-api` depends on `db-migrator` (good) and `sympy-sidecar` (good). But `sympy-sidecar` is `service_started` not `service_healthy`. Sympy needs a proper NATS-connected healthcheck (currently tests socket-only).
- Fix: expose a `/healthz` HTTP endpoint in sympy sidecar (aiohttp) so Docker's healthcheck reports real NATS-subscribed state. Upgrade compose dependency to `service_healthy`.
- Acceptance: `docker compose ps` shows all three APIs `healthy`, not just `Up`.

### Phase 2 — Firebase emulator + dev auth (2-3 hours)

**2.1 Emulator container**

- Add `firebase-emulator` service to `docker-compose.app.yml` using `andreysenov/firebase-tools` (or `google/firebase-tools:13`). Expose auth port (9099) + emulator UI (4000).
- Seed a `firebase.json` with auth emulator config (no Firestore needed for auth-only).

**2.2 Patch `AddFirebaseAuth` to honour the emulator**

- `FirebaseAuthExtensions.cs` currently pins authority to `securetoken.google.com`. Modify to:
  - Detect `FIREBASE_AUTH_EMULATOR_HOST` env var (the canonical Firebase emulator switch).
  - When set, override `options.Authority`, `ValidIssuer` → `http://<host>/www.googleapis.com/identitytoolkit/v3/relyingparty/publicKeys` equivalent (actually: the emulator exposes JWKS at `http://<host>/emulator/v1/projects/{projectId}`).
  - Relax `ValidateLifetime` + accept the emulator's signing key.
- Keep the production path untouched. Guard the change so a missing env var = production behaviour.

**2.3 Update `FirebaseAdminService` bootstrap**

- `FirebaseApp.Create` with default creds tries to call Google. Admin SDK already honours `FIREBASE_AUTH_EMULATOR_HOST` natively (no code change needed).
- Verify the Admin SDK points to the emulator for `CreateUserAsync` / `SetCustomUserClaimsAsync`.

**2.4 Dev-users seed**

Create `src/tools/Cena.Tools.DevUsers/` with a console command that:

- Waits for Firebase emulator healthy
- Creates 10 dev users via `FirebaseAuth.DefaultInstance.CreateUserAsync`:
  - 1 super-admin (`admin@cena.local` / `DevAdmin123!`)
  - 1 curriculum-admin (`curriculum@cena.local` / `DevCur123!`)
  - 2 teachers (`teacher1@cena.local`, `teacher2@cena.local`)
  - 5 students (`student1..5@cena.local`, each `DevStudent123!`)
  - 1 parent (`parent1@cena.local`)
- Sets custom claims (`role`, `tenant_id`, `school_id`, `grade`) on each
- Creates the matching `Tenant` + `School` rows in Postgres via Admin API or direct SQL seed

Acceptance: after boot + seed, a curl against Admin API with a Firebase-emulator-minted JWT for `admin@cena.local` returns 200 on `/api/admin/users`.

### Phase 3 — SPAs + verified workflow (1-2 hours)

**3.1 Add admin + student SPA compose entries**

- Use existing `src/admin/full-version/docker-compose.dev.yml` + `src/student/full-version/docker-compose.dev.yml` as templates.
- Point SPAs at `FIREBASE_AUTH_EMULATOR_HOST` so the JS SDK uses emulator tokens.
- Expose: admin SPA on `http://localhost:5175`, student SPA on `http://localhost:5174`.

**3.2 End-to-end smoke script**

Create `scripts/dev-smoke.sh`:

1. `docker compose up -d`, wait for all healthy
2. Run dev-users seed
3. Mint an admin emulator token via REST, hit `/api/admin/users` — expect 200 + users list
4. Mint a student token, hit `/api/sessions/start` — expect 200 + session id
5. Open admin SPA + student SPA in browser (best-effort)
6. Print the user-password-role table

Acceptance: script exits 0; produces a user/password table in terminal.

## Files to Create / Modify

### Create
- `src/shared/Cena.Infrastructure/Hosting/MartenSchemaReadyCheck.cs` — first-to-run hosted service
- `src/shared/Cena.Infrastructure/Hosting/CenaHostDefaults.cs` — shared `UseCenaDevServiceProvider` extension
- `src/tools/Cena.Tools.DevUsers/` — console app + Dockerfile
- `docker/firebase-emulator/firebase.json` — emulator config
- `scripts/dev-smoke.sh` — end-to-end verification
- `docs/engineering/dev-stack-boot.md` — runbook

### Modify
- `src/shared/Cena.Infrastructure/Auth/FirebaseAuthExtensions.cs` — emulator issuer override
- `src/api/Cena.Admin.Api.Host/Program.cs` — use shared `UseCenaDevServiceProvider`; register MartenSchemaReadyCheck first
- `src/api/Cena.Student.Api.Host/Program.cs` — same
- `src/actors/Cena.Actors.Host/Program.cs` — same; audit DI registrations against Admin-host parity
- `docker-compose.app.yml` — add `firebase-emulator`, `dev-users-seed`, `admin-spa`, `student-spa` services

## Acceptance Criteria

- [ ] `docker compose -f docker-compose.yml -f docker-compose.app.yml up -d` brings the full stack to `healthy` within 3 minutes on a cold machine.
- [ ] `docker stats` shows each .NET service idle (<5% CPU) after boot — no restart loop.
- [ ] `scripts/dev-smoke.sh` exits 0 and prints the user table.
- [ ] Admin SPA at `http://localhost:5175` accepts `admin@cena.local` / `DevAdmin123!` and lands on the dashboard.
- [ ] Student SPA at `http://localhost:5174` accepts `student1@cena.local` / `DevStudent123!` and renders a session.
- [ ] CAS gate is `enforce` and green (sympy healthy, bindings populated by seed).

## Out of Scope

- Production Firebase wiring — this task is local-dev only.
- Kubernetes manifests — covered by RDY-025.
- Hardening the Dockerfiles for production (trimmed images, distroless, secret mounts) — covered by RDY-029.

## Notes

- The SymPy sidecar + `.dockerignore` + `docker-compose.app.yml` + Dockerfile fixes from this session are the load-bearing prerequisite. Commit those before starting Phase 1.
- The DI gaps found in this session (`IClock`, `ICostBudgetService`) are symptoms of a broader pattern: `AddCenaAdminServices()` is called from multiple hosts but assumes host-specific registrations exist. Phase 1.3 should audit `AddCenaAdminServices` / `AddCenaStudentServices` for implicit host dependencies and either fold the missing registrations in, or refactor into per-host extension methods with explicit dependency lists.
- Don't bypass the crashes with `ValidateOnBuild=false` in Production — that's a stub pattern. Development-only override is the honest fix; production should validate.
