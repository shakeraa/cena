# PP-007: Create DB-03, DB-04, DB-07 Migration Safety Net Tasks

- **Priority**: High — prevents silent schema drift in staging/production
- **Complexity**: Architect level — involves Marten configuration, CI pipeline, deploy orchestration
- **Source**: Expert panel review § DB Migration Discipline (Oren)

## Problem

DB-02 (Cena.Db.Migrator with DbUp) is complete and tested. However, the three tasks that make the migrator meaningful were never created in the task queue:

1. **DB-03**: Flip `AutoCreate.None` on Marten in non-dev environments so it stops silently creating/altering tables at startup
2. **DB-04**: CI schema drift gate that fails the build if any Marten document property was added without a corresponding migration script
3. **DB-07**: Deploy sequencing ensuring the migrator container runs to completion before application host containers start

Without these three, a developer can still add a nullable property to a Marten document, have Marten silently create the column in dev, then deploy to staging where the column does not exist (because the migrator has no script for it).

## Scope

### DB-03: AutoCreate.None in Production

**File**: `src/actors/Cena.Actors.Host/Program.cs`, `src/api/Cena.Student.Api.Host/Program.cs`, `src/api/Cena.Admin.Api.Host/Program.cs`

1. Add environment-conditional Marten configuration:
   ```csharp
   opts.AutoCreateSchemaObjects = builder.Environment.IsDevelopment()
       ? AutoCreate.CreateOrUpdate
       : AutoCreate.None;
   ```
2. In non-dev environments, Marten will throw on startup if the schema doesn't match — this is the desired behavior that forces all schema changes through the migrator
3. Add a startup health check that verifies the schema version matches the expected migration version
4. Document the migration workflow in `docs/tasks/infra-db-migration/README.md`

### DB-04: CI Schema Drift Gate

**New file**: `scripts/ci/schema-drift-check.sh` or equivalent

1. In CI, start a fresh PostgreSQL container
2. Run the Db.Migrator to apply all migration scripts
3. Start the Actor Host with `AutoCreate.None` and verify it does not throw
4. If the host throws a schema mismatch error, the CI job fails with a message: "Schema drift detected — add a migration script for your new document properties"
5. This runs on every PR that modifies files under `src/shared/Cena.Infrastructure/Documents/`

### DB-07: Deploy Sequencing

**File**: Docker Compose, Fly.io deploy config, or equivalent orchestration

1. The migration container must run to completion (exit 0) before any application host container starts
2. In Docker Compose: use `depends_on` with `condition: service_completed_successfully`
3. In Fly.io/Railway: use a pre-deploy hook or init container pattern
4. Add a retry mechanism: if migration fails, retry 3 times with exponential backoff before failing the deploy
5. If migration ultimately fails, the deploy is aborted — do not start hosts with a mismatched schema

## Files to Create/Modify

- `src/actors/Cena.Actors.Host/Program.cs` — environment-conditional AutoCreate
- `src/api/Cena.Student.Api.Host/Program.cs` — same
- `src/api/Cena.Admin.Api.Host/Program.cs` — same
- `scripts/ci/schema-drift-check.sh` — NEW: CI drift gate script
- `docker-compose.yml` (or equivalent) — migration sequencing
- `docs/tasks/infra-db-migration/README.md` — updated workflow documentation

## Acceptance Criteria

- [ ] Non-dev environments use `AutoCreate.None`
- [ ] Dev environments continue to use `AutoCreate.CreateOrUpdate` for convenience
- [ ] CI pipeline fails if a document property is added without a migration script
- [ ] Migration container runs before application hosts in all deploy configurations
- [ ] Retry mechanism for migration failures (3 attempts, exponential backoff)
- [ ] README documents the complete migration workflow for new developers
