# Deploy Runbook

**Status**: STUB — real content lands in DB-07 (deployment sequencing).

## Role timeouts (DB-08 Phase 1)

`src/infra/docker/init-db.sql` creates three Postgres roles with the
following `statement_timeout` enforced via `ALTER ROLE ... SET`:

| Role | statement_timeout | Used by |
|---|---|---|
| `cena_student` | 5s | `Cena.Student.Api.Host` (OLTP hot path) |
| `cena_admin` | 60s | `Cena.Admin.Api.Host` (dashboards + batch) |
| `cena_migrator` | unlimited | `Cena.Db.Migrator` (schema changes, from DB-02) |

**Escape hatch for legitimate long-running admin batches**: open a transaction
and `SET LOCAL statement_timeout = 0`. Document every use in this runbook
once the runbook is fleshed out in DB-07. Do NOT `ALTER ROLE` the timeout
away — role-level enforcement is the whole point of the isolation.

## Deferred to later tasks

- PgBouncer pool isolation (DB-08b, blocked on PgBouncer existing in the stack)
- K8s secret manifests for role passwords (DB-07)
- Grafana panels for per-role concurrency (future observability task)
- Password rotation procedure (needs a secret manager first)
- Staging deploy verification (needs a staging environment)
