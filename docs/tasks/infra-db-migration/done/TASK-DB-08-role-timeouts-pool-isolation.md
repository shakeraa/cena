# TASK-DB-08: Per-Role Statement Timeouts + PgBouncer Pool Isolation

**Priority**: HIGH — cheap insurance against "admin query takes down student traffic"
**Effort**: 1 day
**Depends on**: DB-06 (host split must be in place to benefit from per-host roles)
**Track**: D (standalone, ships after the host split)
**Status**: Not Started

---

## You Are

A Postgres operator who has watched a single unindexed admin `JOIN` pin the primary and take down user-facing traffic. You know you can't prevent developers from writing heavy queries, but you can guarantee those queries die before they hurt anyone else. You fix this at the role and pool layer, not at the application layer, because application-layer rate limits can be bypassed and role grants cannot.

## The Problem

Even with the student and admin hosts split (DB-06), they still share:
- One Postgres primary
- One buffer cache
- One autovacuum budget
- One connection pool (if configured naively)

So a single runaway admin query — a nightly stagnation job that forgets to use an index, a moderation dashboard that scans the whole event store, an ad-hoc `SELECT` a developer ran via the admin host — **starves the student's OLTP traffic** because they compete for the same resources on the same database connections.

We deliberately chose not to split the DB or add replicas yet (see ADR / memory). The cheapest way to guarantee blast-radius isolation without either of those is:

1. **Give each host its own Postgres role** with different privileges and different timeouts.
2. **Give each host its own PgBouncer pool** so connection exhaustion on one side cannot starve the other.

This is pure configuration. No schema change, no replication, no new services.

## Your Task

### 1. Introduce two dedicated Postgres roles

Add to a new migration under `db/migrations/V00XX__host_roles_and_timeouts.sql`:

```sql
-- Student host role: hot path, strict timeouts, OLTP-shaped workload
CREATE ROLE cena_student LOGIN PASSWORD :'cena_student_password';
GRANT USAGE ON SCHEMA cena TO cena_student;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA cena TO cena_student;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA cena TO cena_student;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cena_student;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena
  GRANT USAGE, SELECT ON SEQUENCES TO cena_student;

ALTER ROLE cena_student SET statement_timeout = '5s';
ALTER ROLE cena_student SET idle_in_transaction_session_timeout = '10s';
ALTER ROLE cena_student SET lock_timeout = '2s';

-- Admin host role: wider queries, looser timeouts, fewer callers
CREATE ROLE cena_admin LOGIN PASSWORD :'cena_admin_password';
GRANT USAGE ON SCHEMA cena TO cena_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA cena TO cena_admin;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA cena TO cena_admin;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cena_admin;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena
  GRANT USAGE, SELECT ON SEQUENCES TO cena_admin;

ALTER ROLE cena_admin SET statement_timeout = '60s';
ALTER ROLE cena_admin SET idle_in_transaction_session_timeout = '120s';
ALTER ROLE cena_admin SET lock_timeout = '5s';

-- Migrator role: no timeout — schema changes may legitimately be slow
CREATE ROLE cena_migrator LOGIN PASSWORD :'cena_migrator_password';
GRANT ALL PRIVILEGES ON SCHEMA cena TO cena_migrator;
GRANT CREATE ON DATABASE cena TO cena_migrator;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA cena TO cena_migrator;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA cena TO cena_migrator;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena
  GRANT ALL ON TABLES TO cena_migrator;
```

**Timeout rationale**:

| Role | `statement_timeout` | Why |
|---|---|---|
| `cena_student` | 5 s | Student endpoints are OLTP. Anything taking > 5 s is a bug or a DoS and should die. |
| `cena_admin` | 60 s | Admin dashboards aggregate; 60 s is generous for interactive use. Batch jobs that need longer should explicitly `SET statement_timeout = 0` in a transaction. |
| `cena_migrator` | none | Schema reconciliation can run arbitrarily long. |

Tune these with real traces later — the starting values are conservative and can relax if they cause false positives in load tests.

### 2. Update host connection strings

Both new hosts from DB-06 must connect using the new role:

- `Cena.Student.Api.Host` → `cena_student`
- `Cena.Admin.Api.Host` → `cena_admin`
- `Cena.Db.Migrator` → `cena_migrator`

Update `appsettings.json` connection strings and the corresponding Kubernetes secrets. Do NOT keep the shared `cena` superuser role available to the hosts in prod — strip it from the secret-injection pipeline once the new roles are verified in staging.

### 3. PgBouncer pool isolation

Introduce two named pools in PgBouncer config (`pgbouncer.ini` or the Helm values):

```ini
[databases]
cena_student = host=postgres-primary dbname=cena user=cena_student pool_size=40 reserve_pool_size=5
cena_admin   = host=postgres-primary dbname=cena user=cena_admin   pool_size=15 reserve_pool_size=2
cena_migrator = host=postgres-primary dbname=cena user=cena_migrator pool_size=3

[pgbouncer]
pool_mode = transaction
max_client_conn = 500
default_pool_size = 20
```

Pool sizing rationale:

| Pool | `pool_size` | Reasoning |
|---|---|---|
| `cena_student` | 40 | Hot path, many concurrent users. Scale up if `pgbouncer.pools.sv_active` regularly saturates. |
| `cena_admin` | 15 | Few admins, few dashboards, mostly cached. |
| `cena_migrator` | 3 | Only one migrator runs at a time; 3 is headroom for parallel DbUp batches. |

Hosts connect to the named pool, not directly to Postgres:

```
Host=pgbouncer;Port=6432;Database=cena_student;Username=cena_student;...
Host=pgbouncer;Port=6432;Database=cena_admin;Username=cena_admin;...
```

This gives you the second layer of isolation: even if admin exhausts its 15 connections because someone started a hanging transaction, the student pool's 40 connections are completely untouched.

### 4. Verify in staging

Run three tests against staging:

1. **Timeout works**: open a `psql` session as `cena_admin`, run `SELECT pg_sleep(120)`, verify it's killed at 60 s with `ERROR: canceling statement due to statement timeout`. Repeat for `cena_student` with `pg_sleep(10)`.
2. **Pool isolation works**: from a test runner, open 15 idle transactions on the admin pool. Simultaneously run student load. Verify student p99 is unchanged and that new admin connections either queue or fail cleanly.
3. **Migrator still works**: deploy the migrator, verify it completes a schema change using `cena_migrator` without hitting a timeout.

### 5. Observability

Add Grafana panels (or equivalent) for:

- `pg_stat_activity` grouped by `usename` — see per-role concurrency
- `pgbouncer.pools` active/waiting per pool
- Per-role `statement_timeout` cancellations (from `pg_stat_database_conflicts` + logs)

Alerts:

- `cena_admin` statement timeout rate > 5/min → page someone to look at the offending query
- `cena_student` statement timeout rate > 0 → **page immediately**, this should never happen
- Admin pool saturation > 80 % for 5 min → warn on-call

### 6. Runbook entry

Add a short section to [docs/operations/deploy-runbook.md](../../operations/deploy-runbook.md) (file created in DB-07) on:

- How to temporarily raise `statement_timeout` for a one-off admin backfill (`SET LOCAL` in a transaction, never `ALTER ROLE`).
- How to rotate the role passwords.
- What to do when a student-pool alert fires (it shouldn't — investigate, never relax the timeout).

## Files You Must Create

- `db/migrations/V00XX__host_roles_and_timeouts.sql` (pick the next available version number after the ones created in DB-01 and DB-02)

## Files You Must Modify

- `src/api/Cena.Student.Api.Host/appsettings.json` + environment-specific overrides
- `src/api/Cena.Admin.Api.Host/appsettings.json` + environment-specific overrides
- `src/api/Cena.Db.Migrator/appsettings.json` + environment-specific overrides
- PgBouncer config (`deploy/helm/.../pgbouncer-config.yaml` or equivalent)
- Kubernetes secrets manifests (new `cena_student_password`, `cena_admin_password`, `cena_migrator_password`)
- Grafana dashboard JSON (new panels + alerts)
- `docs/operations/deploy-runbook.md`

## Files You Must Read First

- [TASK-DB-06-split-hosts.md](TASK-DB-06-split-hosts.md) — this task assumes the hosts are already split
- [TASK-DB-02-migrator-project.md](TASK-DB-02-migrator-project.md) — migrator role expectations
- Current PgBouncer config (wherever it lives) — understand the existing pool shape before changing it
- [src/shared/Cena.Infrastructure/Configuration/CenaDataSourceFactory.cs](../../../src/shared/Cena.Infrastructure/Configuration/CenaDataSourceFactory.cs) — connection string construction
- [src/actors/Cena.Actors/Configuration/MartenConfiguration.cs](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs) — confirm nothing there relies on superuser privileges Marten doesn't actually need

## Acceptance Criteria

- [ ] Migration creates `cena_student`, `cena_admin`, and `cena_migrator` roles with the correct grants.
- [ ] `statement_timeout`, `idle_in_transaction_session_timeout`, and `lock_timeout` are set via `ALTER ROLE ... SET`, persisted in `pg_db_role_setting`.
- [ ] `Cena.Student.Api.Host` connects as `cena_student`.
- [ ] `Cena.Admin.Api.Host` connects as `cena_admin`.
- [ ] `Cena.Db.Migrator` connects as `cena_migrator`.
- [ ] The shared `cena` superuser role is no longer used by any host in staging or prod.
- [ ] PgBouncer has three distinct pools (`cena_student`, `cena_admin`, `cena_migrator`) with the pool sizes above.
- [ ] Hosts connect through PgBouncer, not directly to Postgres.
- [ ] Timeout test: admin query at 61 s is cancelled; student query at 6 s is cancelled.
- [ ] Pool isolation test: saturating the admin pool does not affect student p99 under load.
- [ ] Migrator completes a real schema change without timing out.
- [ ] Grafana panels show per-role concurrency and per-pool saturation.
- [ ] Alert on student-pool timeout fires during the deliberate test and is acknowledged.
- [ ] Runbook entry written and linked from the main operations doc.
- [ ] Rollback plan: reverting the host `appsettings.json` to use the superuser role restores the old behavior (documented but must not be done except in emergencies).

## Out of Scope

- Read replicas — deferred until workload metrics demand them.
- Content catalog split — separate future task.
- Application-level query cost budgeting — belongs in Marten / EF Core layer, future task.
- Per-endpoint Postgres roles (finer granularity than per-host) — overkill for current scale.

## Notes

- **Do not** set `statement_timeout` in application code. Application-level timeouts are advisory and easy to bypass. Role-level timeouts are enforced by Postgres itself and survive any code change.
- `SET LOCAL statement_timeout = 0` inside a transaction is the escape hatch for legitimate long-running admin batch jobs. Use sparingly and document each use in the runbook.
- The `cena_student` 5 s timeout may feel aggressive. The correct answer if it causes false positives is to **fix the query** or **move it to an async projection**, not to relax the timeout.
