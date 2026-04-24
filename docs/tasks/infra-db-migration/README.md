# Infrastructure — DB Migration & Host Split

**Source**: Session 2026-04-10 — discovery during student web interface planning
**Date**: 2026-04-10
**Architect**: Lead Senior Architect review
**Status**: Ready for implementation

---

## Overview

Nine tasks across four tracks to (a) fix real schema-drift bugs in the current event store, (b) introduce a disciplined migration story before the student/admin host split lands, (c) execute the split itself with shared domain and isolated HTTP edges, and (d) harden workload isolation on the shared database without splitting or replicating it.

**Total estimated effort**: 16-22 days sequential, ~10-13 days with parallelism.

Triggered by:

- Planning the student web interface under [docs/student/](../../student/README.md)
- Decision to split `Cena.Api.Host` into separate student and admin hosts
- Audit of current schema management in [MartenConfiguration.cs](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs), [PgVectorMigrationService.cs](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs), [init-db.sql](../../../src/infra/docker/init-db.sql), and [001_pgvector_embeddings.sql](../../../scripts/sql/001_pgvector_embeddings.sql)

---

## Tasks

| ID | Task | Effort | Priority | Track |
|----|------|--------|----------|-------|
| [DB-00](TASK-DB-00-pgvector-dimension-drift.md) | Fix pgvector dimension drift (384 vs 1536) | 0.5d | CRITICAL | A |
| [DB-01](TASK-DB-01-unify-pgvector-ddl.md) | Unify pgvector DDL into one authoritative location | 1d | HIGH | A |
| [DB-02](TASK-DB-02-migrator-project.md) | Create `Cena.Db.Migrator` console app + DbUp | 2-3d | HIGH | B |
| [DB-03](TASK-DB-03-autocreate-none-prod.md) | Flip prod Marten from `CreateOrUpdate` to `None` | 1-2d | HIGH | B |
| [DB-04](TASK-DB-04-schema-drift-ci-gate.md) | CI schema-drift gate (`AssertDatabaseMatchesConfiguration`) | 1-2d | MEDIUM | B |
| [DB-05](TASK-DB-05-contracts-library.md) | Extract `Cena.Api.Contracts` shared library | 2-3d | HIGH | C |
| [DB-06](TASK-DB-06-split-hosts.md) | Split hosts into `Cena.Student.Api.Host` + `Cena.Admin.Api.Host` | 4-6d | HIGH | C |
| [DB-07](TASK-DB-07-deployment-sequencing.md) | Deploy sequencing (migrator → student → admin) | 2-3d | MEDIUM | C |
| [DB-08](TASK-DB-08-role-timeouts-pool-isolation.md) | Per-role statement timeouts + PgBouncer pool isolation | 1d | HIGH | D |

---

## Dependency Graph

```text
Track A (quick wins, ~1.5d):
  DB-00 ── DB-01
                  \
Track B (migration discipline, ~5-7d):
  DB-02 (migrator app)
      │
      ▼
  DB-03 (flip AutoCreate.None)
      │
      ▼
  DB-04 (CI drift gate)
                        \
Track C (host split, ~8-12d):   ────▶ DB-05 ── DB-06 ── DB-07
                                                      │
Track D (workload isolation, ~1d):                    └─▶ DB-08
```

- **Track A** is independent and can land in a single PR.
- **Track B** depends on Track A's unified DDL to seed the first migration.
- **Track C** depends on Track B being in place — splitting hosts without migration discipline amplifies drift risk.
- **Track D** depends on Track C — per-host Postgres roles only make sense once each host is a distinct process with its own connection string.

---

## Parallel Tracks

- Track A is trivial and should merge immediately regardless of the host split.
- Track B + Track C can run in parallel **until** DB-06 starts — at which point Track B must be done, because the new hosts boot with `AutoCreate.None`.
- DB-05 can begin as soon as DB-02 merges (contracts extraction is independent of migrator logic).
- DB-08 is a single-day task that must wait for DB-06 because it introduces per-host Postgres roles — without two hosts, there's nothing to isolate.

---

## Related Docs

- [docs/student/00-overview.md](../../student/00-overview.md) — student web stack + deployment context
- [docs/student/15-backend-integration.md](../../student/15-backend-integration.md) — full REST + Hub contract surface
- [docs/tasks/student-web/README.md](../../../tasks/student-web/README.md) — student web UI task bundle (depends on DB-06)
- [docs/tasks/student-backend/README.md](../../../tasks/student-backend/README.md) — student backend task bundle (depends on DB-05 and DB-06)
- [docs/architecture-design.md](../../architecture-design.md) — current system architecture
- [docs/adaptive-learning-architecture-research.md](../../adaptive-learning-architecture-research.md) — event sourcing rationale

---

## Discovered Issues (Source Material)

Captured during the 2026-04-10 architecture conversation. Every task below traces to a concrete finding:

1. **pgvector dimension drift** — [init-db.sql:26](../../../src/infra/docker/init-db.sql#L26) declares `vector(384)` but [001_pgvector_embeddings.sql:14](../../../scripts/sql/001_pgvector_embeddings.sql#L14) and [PgVectorMigrationService.cs:52](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs#L52) declare `vector(1536)`. Inconsistent between local dev and deployed environments. → DB-00
2. **Three sources of pgvector DDL** — `init-db.sql`, `001_pgvector_embeddings.sql`, and `PgVectorMigrationService.cs` all own the same table with different shapes. → DB-01
3. **No migration runner** — no EF Core, DbUp, FluentMigrator, or Grate; no `__migrations` tracking table; no version history. → DB-02
4. **`AutoCreate.CreateOrUpdate` in prod** — [MartenConfiguration.cs:49](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs#L49) lets any host deploy mutate the schema at boot. → DB-03
5. **No CI drift detection** — a PR can ship a breaking schema change without warning. → DB-04
6. **No shared contracts project** — host split will duplicate DTOs + event registrations if not extracted first. → DB-05
7. **Admin + student served from one host** — [Program.cs](../../../src/api/Cena.Api.Host/Program.cs) maps both admin and student endpoint groups in the same process. Blast radius problem for the student split. → DB-06
8. **No deploy ordering discipline** — today there's one host, tomorrow there are three (migrator + student + admin) and nothing enforces order. → DB-07
9. **Shared Postgres role + shared connection pool** — both hosts will initially connect as the same user with the same `statement_timeout` and the same PgBouncer pool. A runaway admin query can starve student traffic on the shared primary. Per-role timeouts + per-pool isolation prevent that without needing replicas or a DB split. → DB-08
