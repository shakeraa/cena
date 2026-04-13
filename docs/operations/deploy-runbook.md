# Deploy Runbook

Cena Platform deployment guide covering normal deploys, emergency rollback,
manual migrator execution, and troubleshooting.

**Helm chart**: `deploy/helm/cena/`
**CD workflow**: `.github/workflows/cd-deploy.yml`
**Grafana dashboard**: `deploy/observability/dashboards/cena-deploy-health.json`
**Alerting rules**: `deploy/observability/alerting-rules.yaml`

---

## Deploy sequence

Every deployment enforces a strict 3-phase order:

```
1. Migrator Job (Helm pre-install/pre-upgrade hook)
   +-- Runs all pending db/migrations/*.sql via DbUp
   +-- On failure: Helm aborts -- no host is touched

2. Student API Host (Rolling update: maxSurge=25%, maxUnavailable=0)
   +-- Readiness probe: GET /health/ready
   +-- Includes AssertDatabaseMatchesConfiguration check

3. Admin API Host (Rolling update, same strategy)
   +-- initContainer waits for student /health/ready + 60s soak
   +-- Only starts after student is confirmed healthy
```

The Helm `--atomic` flag ensures that if any phase fails, the entire release
is rolled back automatically.

---

## Normal deploy flow

### Via GitHub Actions (recommended)

1. Go to **Actions > CD -- Deploy to Kubernetes**.
2. Click **Run workflow**.
3. Select the environment (`staging` or `production`).
4. Enter the image tag (e.g. `sha-abc1234` or `v1.2.3`).
5. Click **Run workflow** and monitor the job.

### Via CLI

```bash
# Staging
helm upgrade --install cena deploy/helm/cena/ \
  --namespace cena-staging \
  --create-namespace \
  --set migrator.image.tag=<TAG> \
  --set student.image.tag=<TAG> \
  --set admin.image.tag=<TAG> \
  --timeout 10m \
  --wait \
  --atomic

# Production (same command, different namespace)
helm upgrade --install cena deploy/helm/cena/ \
  --namespace cena-production \
  --set migrator.image.tag=<TAG> \
  --set student.image.tag=<TAG> \
  --set admin.image.tag=<TAG> \
  --timeout 10m \
  --wait \
  --atomic
```

### What to watch during deploy

- Migrator job pod logs: `kubectl logs -n cena-<env> job/cena-migrator -f`
- Student rollout: `kubectl rollout status -n cena-<env> deploy/cena-student`
- Admin rollout: `kubectl rollout status -n cena-<env> deploy/cena-admin`
- Grafana dashboard: **Cena Deploy Health** (UID: `cena-deploy-health`)

---

## Emergency rollback

```bash
# List release history
helm history cena --namespace cena-<env>

# Roll back to the previous release
helm rollback cena --namespace cena-<env> --wait --timeout 5m

# Roll back to a specific revision
helm rollback cena <REVISION> --namespace cena-<env> --wait --timeout 5m
```

The rollback will:
1. Re-run the migrator hook. Since migrations are idempotent (DbUp tracks
   applied scripts in `cena.schemaversions`), this is a no-op.
2. Roll student pods back to the previous image.
3. Roll admin pods back to the previous image.

**If the schema change was backwards-incompatible** (this should never happen
since migrations are append-only, but just in case):
1. Write a compensating migration: `db/migrations/V{N+1}__revert_<description>.sql`
2. Build a new migrator image with the revert script.
3. Deploy the revert as a normal release.

---

## Manual migrator execution

For ad-hoc migration runs outside of Helm (e.g. against a dev database):

```bash
# Using the Docker image
docker run --rm \
  -e CENA_MIGRATOR_CONNECTION_STRING="Host=<host>;Port=5432;Database=cena;Username=cena_migrator;Password=<pwd>" \
  ghcr.io/cena-platform/cena-db-migrator:<tag>

# Using dotnet directly (from repo root)
dotnet run --project src/api/Cena.Db.Migrator \
  "Host=localhost;Port=5433;Database=cena;Username=cena_migrator;Password=<pwd>"

# Or via environment variable
export CENA_MIGRATOR_CONNECTION_STRING="Host=localhost;Port=5433;Database=cena;Username=cena_migrator;Password=<pwd>"
dotnet run --project src/api/Cena.Db.Migrator
```

The migrator looks for the connection string in this priority order:
1. CLI argument (first positional arg)
2. `CENA_MIGRATOR_CONNECTION_STRING` env var
3. `ConnectionStrings__cena_migrator` env var

---

## Interpreting AssertDatabaseMatchesConfiguration failures

When a host's `/health/ready` endpoint returns unhealthy with a schema assertion
error, it means the running .NET code expects a database schema that does not
match what is actually in PostgreSQL.

**Common causes:**

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Missing table/column | Migrator did not run or a migration is missing | Run migrator manually, verify `cena.schemaversions` |
| Extra column in DB | Code is older than the schema | Deploy the correct image version that matches the schema |
| Type mismatch | Migration altered a column type | Check the migration history, may need a compensating migration |
| Marten document table missing | Marten auto-create is set to None but the table was never created | Temporarily set AutoCreate to CreateOrUpdate, start host once, then revert |

**Diagnostic steps:**

```bash
# Check what migrations have been applied
psql -U cena_migrator -d cena -c "SELECT * FROM cena.schemaversions ORDER BY schemaversionsid;"

# Check the migrator job logs
kubectl logs -n cena-<env> job/cena-migrator

# Check the failing host logs
kubectl logs -n cena-<env> deploy/cena-student --tail=50
kubectl logs -n cena-<env> deploy/cena-admin --tail=50

# Describe the pod to see events
kubectl describe pod -n cena-<env> -l app.kubernetes.io/component=student
```

---

## How to add a new migration

See [db/migrations/README.md](../../db/migrations/README.md) for full details. Summary:

1. Find the highest `V{N}__*.sql` in `db/migrations/`.
2. Create `V{N+1}__your_description.sql`.
3. Use `IF NOT EXISTS` on all DDL statements.
4. Test locally: `psql -U cena_migrator -d cena -f db/migrations/V{N+1}__your_description.sql`
5. Commit in a dedicated PR. Never edit existing migration files.
6. The next deploy will pick up the new migration automatically via the Helm hook.

---

## What to do if the migrator succeeds but a host refuses to start

1. Check host pod logs for the specific error.
2. If it is a Marten schema mismatch (not a raw SQL migration issue), the
   problem is in C# model configuration, not in `db/migrations/`.
3. Verify that the host image tag matches the migrator image tag -- a version
   skew can cause this.
4. If Marten expects a table that does not exist, the migration may be missing.
   Check `cena.schemaversions` against the `db/migrations/` folder.
5. As a last resort, temporarily set `AutoCreate = AutoCreate.CreateOrUpdate`
   in the host configuration, deploy once to let Marten create its tables,
   then revert to `AutoCreate.None`.

---

## Role timeouts (DB-08 Phase 1)

`src/infra/docker/init-db.sql` creates three Postgres roles with the
following `statement_timeout` enforced via `ALTER ROLE ... SET`:

| Role | statement_timeout | Used by |
|---|---|---|
| `cena_student` | 5s | `Cena.Student.Api.Host` (OLTP hot path) |
| `cena_admin` | 60s | `Cena.Admin.Api.Host` (dashboards + batch) |
| `cena_migrator` | unlimited | `Cena.Db.Migrator` (schema changes, from DB-02) |

**Escape hatch for legitimate long-running admin batches**: open a transaction
and `SET LOCAL statement_timeout = 0`. Document every use in this runbook.
Do NOT `ALTER ROLE` the timeout away -- role-level enforcement is the whole
point of the isolation.

---

## Kubernetes Secret structure

The Helm chart expects a secret named per `database.secretName` (default:
`cena-db-credentials`) with three keys:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: cena-db-credentials
type: Opaque
stringData:
  connection-string-migrator: "Host=...;Database=cena;Username=cena_migrator;Password=..."
  connection-string-student:  "Host=...;Database=cena;Username=cena_student;Password=..."
  connection-string-admin:    "Host=...;Database=cena;Username=cena_admin;Password=..."
```

Create this secret before deploying. In production, use a secret manager
(e.g. External Secrets Operator, Sealed Secrets) instead of plain manifests.

---

## Deferred to later tasks

- PgBouncer pool isolation (DB-08b, blocked on PgBouncer existing in the stack)
- Grafana panels for per-role concurrency (future observability task)
- Password rotation procedure (needs a secret manager first)
- Canary deploys / traffic splitting (future work)
- Multi-region deploy orchestration (future work)
