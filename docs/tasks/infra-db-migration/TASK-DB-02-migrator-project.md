# TASK-DB-02: Create `Cena.Db.Migrator` Console App with DbUp + Marten

**Priority**: HIGH — prerequisite for `AutoCreate.None` and the host split
**Effort**: 2-3 days
**Depends on**: DB-01
**Track**: B
**Status**: Not Started

---

## You Are

A senior .NET engineer who has seen production outages caused by hosts racing each other to mutate a schema at boot. You build deployment tooling that is explicit, ordered, and safe to re-run. You do not trust `AutoCreate.CreateOrUpdate` in any environment that matters.

## The Problem

Today, schema evolution happens in two uncoordinated places:

1. **Marten**, via [MartenConfiguration.cs:49](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs#L49) (`AutoCreateSchemaObjects = CreateOrUpdate`) — every process boot can ALTER tables in prod.
2. **Raw SQL**, via `PgVectorMigrationService` — runs at host start, idempotent by convention.

There is no version history (`__migrations` table), no rollback plan, no CI gate to catch breaking changes, and no ordering guarantee between hosts. This is tolerable with one host today. It is not tolerable the day the student/admin split ships (DB-06) because both hosts will race at boot.

## Your Task

Create a dedicated migration runner project that is the **single authoritative source of schema changes** and can be run as a Kubernetes Job / Docker init container before any app host boots.

### 1. Project skeleton

```
src/api/Cena.Db.Migrator/
├── Cena.Db.Migrator.csproj
├── Program.cs
├── MartenMigrator.cs
├── SqlMigrator.cs
├── appsettings.json               (only connection string + logging)
└── Dockerfile
```

Framework: **.NET 9** (matches rest of solution). Dependencies:
- `Marten` (same version as `Cena.Api.Host`)
- `dbup-postgresql`
- `Npgsql`
- `Microsoft.Extensions.Hosting`
- `Cena.Actors` (for `MartenConfiguration.ConfigureCenaEventStore`)
- `Cena.Infrastructure` (for shared pieces)

### 2. Responsibilities

On run:

1. **Open a connection** via the shared `CenaDataSourceFactory`.
2. **Run raw SQL migrations** using DbUp, reading files from `db/migrations/*.sql` as an embedded resource directory.
   - DbUp tracks applied migrations in `cena.__migrations`.
   - Fails fast if a previously applied file's checksum has changed (append-only enforcement).
3. **Run Marten schema reconciliation** by calling `store.Storage.ApplyAllConfiguredChangesToDatabase()`. This is Marten's explicit (not auto) migration call. It uses the same `ConfigureCenaEventStore` as the hosts, so it knows exactly which document types and projections exist.
4. **Print a summary**: SQL migrations applied, Marten objects created/updated, and the final migration tracking state. Exit 0 on success, non-zero on any failure.

### 3. Modes

Add a CLI flag / env var for mode selection:

| Mode | Behavior |
|---|---|
| `apply` (default) | Execute all pending changes. |
| `dry-run` | Print the SQL Marten would run via `store.Storage.WritePatch()` without executing. |
| `validate` | Call `AssertDatabaseMatchesConfiguration()` — exit 0 if DB matches config, exit 1 with a diff if not. Used by CI drift gate (DB-04). |
| `baseline` | Mark all existing `db/migrations/*.sql` files as applied without running them. Used once at rollout. |

### 4. Dockerfile

Multi-stage build, non-root user, minimal runtime image. Must work as a Kubernetes Job (reads `ConnectionStrings__Cena` from env).

### 5. Solution wiring

Add the project to `Cena.sln`. Ensure `dotnet build` at the repo root builds it. Ensure the existing admin and host CI jobs do NOT reference it (it deploys independently).

### 6. Keep `PgVectorMigrationService` temporarily — but gated

The service was loading SQL from disk after DB-01. After DB-02 ships:
- Wrap the service behind a config flag `Database:RunLegacyPgVectorMigration` (default `false`).
- When the migrator is in place, prod sets the flag to `false` and the service becomes a no-op.
- Removal is deferred to a follow-up cleanup PR after two clean deploys confirm the migrator path works.

## Files You Must Create

- `src/api/Cena.Db.Migrator/Cena.Db.Migrator.csproj`
- `src/api/Cena.Db.Migrator/Program.cs`
- `src/api/Cena.Db.Migrator/MartenMigrator.cs`
- `src/api/Cena.Db.Migrator/SqlMigrator.cs`
- `src/api/Cena.Db.Migrator/appsettings.json`
- `src/api/Cena.Db.Migrator/Dockerfile`

## Files You Must Modify

- `Cena.sln` — add the new project
- [src/actors/Cena.Actors/Services/PgVectorMigrationService.cs](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs) — gate behind config flag

## Files You Must Read First

- [src/actors/Cena.Actors/Configuration/MartenConfiguration.cs](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs) — understand the full surface the migrator must reconcile
- [src/shared/Cena.Infrastructure/Configuration/CenaDataSourceFactory.cs](../../../src/shared/Cena.Infrastructure/Configuration/CenaDataSourceFactory.cs) — the data source the migrator should use
- [src/api/Cena.Api.Host/Program.cs](../../../src/api/Cena.Api.Host/Program.cs) — current Marten wiring for reference
- DbUp docs: https://dbup.readthedocs.io/en/latest/ (offline check the nuget readme)
- Marten schema management docs in the Marten repo

## Acceptance Criteria

- [ ] `Cena.Db.Migrator` project builds, runs, and applies all existing schema when pointed at a fresh Postgres.
- [ ] A second run is a no-op (idempotency).
- [ ] `db/migrations/V0001__*.sql` is tracked in `cena.__migrations` after first run.
- [ ] Marten's `ApplyAllConfiguredChangesToDatabase()` is called explicitly, not implicitly.
- [ ] `--dry-run` prints the pending changes without applying them.
- [ ] `--validate` exits 0 on a matching DB and 1 with a diff on a drifted DB.
- [ ] `--baseline` marks existing SQL migrations as applied without running them.
- [ ] Dockerfile produces an image tagged `cena/db-migrator:{version}` that runs as non-root.
- [ ] `PgVectorMigrationService` is gated behind `Database:RunLegacyPgVectorMigration` and defaults to `false`.
- [ ] CI pipeline has a job that runs `Cena.Db.Migrator` against a throwaway Postgres container on every PR.
- [ ] README at `src/api/Cena.Db.Migrator/README.md` documents the modes, the Kubernetes Job pattern, and how to add a new migration.

## Out of Scope

- Flipping prod Marten to `AutoCreate.None` — that's DB-03.
- CI drift gate using `--validate` — that's DB-04.
- Deploying the migrator as a real Kubernetes Job in prod — that's DB-07.
- Removing `PgVectorMigrationService` entirely — follow-up cleanup PR.
