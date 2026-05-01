# Overnight Tasks — Cena Platform

Larger refactors that don't block today's work but are worth running unattended
overnight (or in a quiet window) by an autonomous worker. Each entry lists the
queue task id (so a worker can `node .agentdb/kimi-queue.js claim <id>`), the
goal, scope, and DoD.

The corresponding queue rows are tagged `overnight` so workers can filter:

```bash
node .agentdb/kimi-queue.js list --status pending | grep -i overnight
```

---

## OBS-SEED-BG-001 — Move startup seeders to BackgroundService

**Queue task:** `t_37c9ca5f28aa` (run `node .agentdb/kimi-queue.js show t_37c9ca5f28aa`)

**Why this is a refactor, not a quick fix:**
The 2026-05-01 admin-api hot-loop incident exposed a structural issue: every
seeder in `Cena.Infrastructure/Seed/*` runs synchronously during host
`StartAsync` via `IHostedService.StartAsync`. A slow Postgres or transient
network hiccup blocks Kestrel coming up, which then blocks readyz, which then
flips the whole container unhealthy. The post-incident layers
(`5255453b → 0acaf6c5 → 61c224ea`) closed the silent-burn class via:

- Layer 1: idempotent seeds (skip-on-existing)
- Layer 3: cgroup CPU cap (containment)
- Layer 4: Prometheus alert (detection)
- A: per-query Postgres `Command Timeout=30` (failure-bounded queries)
- B: split `/health/live` (process up) vs `/health/ready` (deps + seeds done)

What's still wrong: a slow seed still blocks `Run()` → Kestrel still doesn't
listen until every `IHostedService.StartAsync` completes. That delays the SPA
and downstream traffic for the duration of the slowest seed.

**Goal:** convert each `Cena.Infrastructure/Seed/*Data` class from a
synchronous startup step into an `IHostedService` that runs *after*
`IHostApplicationLifetime.ApplicationStarted` fires. Kestrel comes up in
seconds; seeds run on a non-request thread with their own per-task budgets.

**Scope:**

1. New abstract base `BackgroundSeedService` that:
   - Subscribes to `IHostApplicationLifetime.ApplicationStarted`
   - Waits a short delay (5s) so boot has settled
   - Runs the seed inside its own `CancellationTokenSource` linked to the host
     stop token, with a per-seed timeout (default 5min, override per-seed)
   - Catches and logs any exception — does NOT crash the host
   - Posts a `ready` signal into a registry (`ISeedReadinessRegistry`) so
     `/health/ready` can wait on all seeds before returning 200

2. Convert the existing seeders, one PR per family:
   - `BagrutCorpusSeedData` → `BagrutCorpusSeedService`
   - `CulturalContextSeeder` (already an `IHostedService` but startup-blocking) → use the new base
   - `LearningObjectiveSeed`
   - Any others under `Cena.Infrastructure/Seed/*Data` and `*Seeder`

3. Add `ISeedReadinessRegistry` health check tagged `seed` that's included in
   `/health/ready`'s default predicate. So `/health/ready` returns 503 until
   all seeds report ready (or a generous global deadline elapses, after which
   we report degraded but ready — partial seeding is better than indefinite
   stuck).

4. Remove all direct seeder calls from `Program.cs` startup code; rely on the
   BackgroundService registration.

5. Tests:
   - Unit test for `BackgroundSeedService` base: cancellation, exception
     swallowing, per-seed timeout, ready-registry post.
   - Integration test that boots admin-api with a deliberately-stuck fake
     seed and asserts: Kestrel binds within 5s, /health/live returns 200
     immediately, /health/ready returns 503 until the seed completes (or
     500 if it times out).

**DoD:**

- All seeders inherit from `BackgroundSeedService`.
- `Program.cs` startup wires only `AddHostedService<...SeedService>()` calls,
  no direct `seed.SeedAsync(...)`.
- Cold-boot Kestrel ready time < 10s (currently ~30-90s on this stack).
- `/health/ready` returns 503 with `seed` health-check details when seeds
  in flight, 200 when all complete.
- `Cena.Admin.Api.Tests` + `Cena.Infrastructure.Tests` + `Cena.Actors.Tests`
  all pass; full sln builds 0 errors.

**Files (likely):**

- `src/shared/Cena.Infrastructure/Seed/BackgroundSeedService.cs` (new)
- `src/shared/Cena.Infrastructure/Seed/ISeedReadinessRegistry.cs` (new)
- `src/shared/Cena.Infrastructure/Seed/BagrutCorpusSeedData.cs` (refactor)
- `src/api/Cena.Admin.Api/CulturalContextSeeder.cs` (refactor)
- `src/api/Cena.Admin.Api.Host/Program.cs` (remove inline seed calls,
  add `AddHostedService<>` registrations)
- `src/api/Cena.Student.Api.Host/Program.cs` (same)
- `src/actors/Cena.Actors.Host/Program.cs` (same)
- Tests in respective `*.Tests` projects.

**Estimated effort:** 3-4 hours for an autonomous worker.

**Reporting:** branch name `<worker>/obs-seed-bg-001`, single commit per
seeder migration if practical, full sln + Admin.Api.Tests + Infrastructure.Tests
green at PR time.
