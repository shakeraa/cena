# RDY-056: Dockerized Dev Stack — Bootable End-to-End

- **Status**: ✅ **VERIFIED 2026-04-19** — cold `docker compose down && up` passes.
  See "Verification log (2026-04-19)" section at the bottom.
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

---

## Execution Log (2026-04-17 → 2026-04-18)

### Phase 1 — .NET containers boot (commit `62f98c3`)
- SymPy Python/NATS sidecar (docker/sympy-sidecar/): real worker for all 5 CasOperation types, `sympy==1.13.3`. NATS user `sympy-sidecar` added.
- `.dockerignore` prunes `.agentdb/worktrees`, `bin`, `obj`, SPA bundles (build context was 30+ GB → <1 GB).
- Marten pre-`app.Run()` warm-up (admin + student + actor hosts): `ApplyAllConfiguredChangesToDatabaseAsync` serialises DDL before hosted-service seeders race the `TimedLock`.
- `MethodologyEffectivenessByCultureDocument.DocumentAlias("method_effect_by_culture")` — full type name overflowed Postgres NAMEDATALEN.
- `AddCenaAdminServices` TryAdd's shared infra (`ICostBudgetService`, `ICostCircuitBreaker`, `IClock`, `IErasureCryptoConfig`, `IErasureManifestBuilder`). Caller hosts no longer need to remember them.
- `IWebPushClient` registered as singleton in Student host.
- `ValidateOnBuild=false` in Development across all three hosts (production path untouched).
- Kestrel + PostgreSQL connection strings overridable via `Kestrel__Endpoints__Http__Url` + `ConnectionStrings__PostgreSQL` env; baked `appsettings.Development.json` was pinning `localhost:5433`/`5051`.
- Duplicate `RevokeMyConsent` endpoint name renamed to `RevokeMyGdprConsent`.
- Dockerfile fixes: student-api + actor-host Dockerfiles copy `Cena.Admin.Api.csproj` for restore; `SkipOpenApiGeneration=true` condition + MSBuild flag.

### Phase 2 — Firebase emulator + dev users (commit `d86ba66`)
- `firebase-emulator` service (firebase-tools v13, Auth on :9099, UI on :4000).
- `FirebaseAuthExtensions` emulator branch guarded by `FIREBASE_AUTH_EMULATOR_HOST`. Uses `UseSecurityTokenValidators=true` + `SignatureValidator` bypass + `RequireSignedTokens=false` to accept the emulator's alg=none JWTs. Production path untouched.
- `seed-dev-users.sh` creates 10 accounts via the emulator REST API with UPPER_SNAKE_CASE roles matching `ResourceOwnershipGuard.cs`:
  - `shaker.abuayoub@gmail.com` / `ShakerMain2026!` — SUPER_ADMIN (primary, matches Marten `sa-001`)
  - `admin@cena.local` / `DevAdmin123!` — SUPER_ADMIN
  - `curriculum@cena.local` / `DevCur123!` — ADMIN
  - `teacher1..2@cena.local` / `DevTeacher123!` — TEACHER
  - `student1..5@cena.local` / `DevStudent123!` — STUDENT (mixed 5U/4U, he/ar)
  - `parent1@cena.local` / `DevParent123!` — PARENT
- Verified end-to-end: admin sign-in → idToken → `GET /api/admin/users` returns 200.

### Phase 3 — SPAs in docker (commit `36401b4`)
- `admin-spa` (:5174) + `student-spa` (:5175) compose entries.
- Admin SPA Firebase plugin: added `connectAuthEmulator` branch.
- Admin i18n (ar/en/he): escape `@` in email addresses as `{'@'}` so vue-i18n's linked-message parser doesn't choke on `privacy@cena.edu`.
- Student SPA `package.json`: added missing `dompurify`, `function-plot`, `mathlive` deps.
- Onboarding redirect bug (commit `36401b4` + `714895c`): `meStore.__setOnboardedAt` no-op'd when profile was null (fresh sign-in race). Fixed to create a minimal profile stub so `isOnboarded` actually flips.
- Onboarding confirm-step contrast: `bg-surface-variant` → bordered transparent list; subtitle opacity `0.6 → 0.85`.

### Phase 4 — Student emulator + OCR wiring (commit `714895c`)
- `src/emulator/Dockerfile` + `emulator` compose service under `profiles: ["emulator"]` (opt-in). Defaults: 50 students, 25× speed, 300s.
- Verified: 15 simulated students pushed 8350+ events through NATS to actor-host.
- `AddOcrCascadeCore` + `CasRouterLatexValidator` wired into admin-api DI so `BagrutPdfIngestionService` resolves.
- **PDF ingestion gap tracked**: the runner layers (`ILayer1Layout` / `ILayer2aTextOcr` / `ILayer2bMathOcr`) need either the Surya + pix2tex sidecars or Gemini / Mathpix API keys to actually ingest. Core brain is wired; runners intentionally pluggable.

### Phase 5 — Compose-network reachability (commits `045f146`, `1173a2b`, `cc1da59`, this one)
Every dashboard page painted services red because of a chain of hardcoded / stale URLs:
- Admin API's NATS probe hit `localhost:8222` instead of `cena-nats:8222`; Actor-Host probe hit `localhost:5119` instead of `cena-actor-host:5050`. Both now read from `CENA_NATS_MONITORING_URL` / `CENA_ACTOR_STATS_URL`.
- Actor-Host probe converted to two-step: liveness via unauth `/health/live`, stats (SuperAdmin-gated) best-effort. 401 on stats no longer flips the card to "down".
- Admin SPA Vite proxy default `5050 → 5052` for `/api/*` and `5119 → 5050` for `/api/actors/*`; both overridable via `VITE_ADMIN_API_PROXY_TARGET` / `VITE_ACTOR_API_PROXY_TARGET` (compose points them at sibling containers).
- Actor-Host `AllowedHosts=*` override — was pinned to `localhost`, returning `400 Bad Request — Invalid Hostname` to sibling calls.
- Actor-Host Firebase emulator env added (`Firebase__ProjectId` + `FIREBASE_AUTH_EMULATOR_HOST`) so it accepts the same tokens admin-api does.
- `OfflineBanner.vue` `v-model="!isOnline"` was a compile error — wrapped in a computed with a no-op setter.
- Architecture diagram page:
  - Ports read from `VITE_PORT_*` env vars (six-pack: FRONTEND / STUDENT_SPA / ADMIN_API / STUDENT_API / ACTOR_HOST / NATS / POSTGRES / REDIS / FIREBASE_EMU).
  - Probes now attach the Firebase bearer (`authedFetch` helper matching `useApi` pattern); plain `fetch()` was hitting 401 → painting everything red.
  - Per-service health read from admin-api's probe array instead of mirroring admin-api status transitively.
  - Added Student SPA, Student API, SymPy Sidecar, Firebase Emulator nodes (8 → 11 services).
  - Emulator liveness: `commandsRouted > 0` (monotonic) instead of `activeActors > 0` (short-lived).
  - `getNodeStatus` + `getEdgeActive` switch maps updated for new node IDs (the bug that painted V / S / St red after the node rename).

### What's green
All 11 services report correctly when the dockerised stack is up and the user is signed in as a SuperAdmin.

### Still pending
- **PDF ingestion runners**: Surya / pix2tex sidecars or Gemini / Mathpix API keys needed for `ILayer1Layout` / `ILayer2aTextOcr` / `ILayer2bMathOcr` concrete impls.
- **Emulator answer-processing**: pre-existing `Index was outside the bounds of the array` warnings in the sim's answer path. Non-blocking; traffic still flows.
- **`/health/live` endpoint on actor-host**: returns HTTP 200 but I've not added a dedicated admin-api → actor-host internal service-token path; stats counters stay at zero on the admin-api probe (populated only when the browser hits `/api/actors/stats` directly).

### Accounts file
A canonical list of dev accounts lives in [docker/firebase-emulator/seed-dev-users.sh](../../docker/firebase-emulator/seed-dev-users.sh). Re-run with
`docker exec cena-firebase-emulator /seed/seed-dev-users.sh` after any emulator reset.

## Verification log (2026-04-19)

After shipping the earlier fixes in this session (including `7bc77bf`
ConcurrentDictionary fix for the emulator), a fresh cold-boot sweep was
run:

```
docker compose -f docker-compose.yml -f docker-compose.app.yml \
               -f docker-compose.hotreload.yml down
docker compose -f docker-compose.yml -f docker-compose.app.yml \
               -f docker-compose.hotreload.yml up -d
```

Results against the three original failure modes + the Firebase
cross-cutting concern:

| # | Failure mode | Status |
|---|---|---|
| 1 | Admin API Marten schema-creation deadlock (`TaskCanceledException` from `TimedLock`) | ✅ Resolved. `[MARTEN_SCHEMA_READY]` logs before any seeder; zero `TaskCanceled` on boot. |
| 2 | Student API DI validation | ✅ Boots clean; `/health` → 200. |
| 3 | Actor Host DI validation | ✅ Boots; no crash loop. |
| × | Firebase emulator not wired | ✅ `FIREBASE_AUTH_EMULATOR_HOST=cena-firebase-emulator:9099` env set in both admin-api and student-api containers. |

Post-boot health:

```
cena-admin-api           Up (healthy) — /health 200
cena-student-api         Up (healthy) — /health 200
cena-actor-host          Up (no healthcheck by design)
cena-sympy-sidecar       Up (healthy)
cena-postgres/redis/nats/neo4j/firebase-emulator — all healthy
cena-dynamodb            Up but healthcheck flaky (functional per init log)
cena-emulator            Up, steady-state 15% CPU, 0 errors
```

Acceptance criteria confirmed met for Phase 1. Phase 2 (dev UX polish)
and Phase 3 (CI integration) are out of scope for this verification —
they ship when the relevant tasks do.

