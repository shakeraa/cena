# Cena Database Migrations

Authoritative, versioned SQL migrations for the Cena platform's PostgreSQL schema. Everything in this folder is the **single source of truth** for raw SQL DDL (outside of Marten's auto-managed document/event tables). Marten itself is configured in `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` and manages its own schema; this folder holds the SQL that Marten does not own — currently pgvector + its content embeddings table.

## Naming convention

```
V{4-digit version}__{snake_case_description}.sql
```

Examples:

- `V0001__pgvector_extension_and_embeddings.sql`
- `V0002__add_session_replay_index.sql`
- `V0003__partition_events_by_month.sql`

Rules:

- **Version numbers are monotonically increasing.** If the highest existing version is `V0042`, the next migration is `V0043`.
- **Zero-padded to 4 digits** for stable sort order in `ls` / file explorers.
- **Snake_case description** after the double underscore. Keep it short (under ~50 chars).
- **Never skip a version.** If `V0005` was reserved but abandoned, still bump to `V0006` for the next real migration.

## Append-only

- **Never edit a file after it has been merged to `main`.** If a change is needed, add a new migration. The history must be replayable from any checkout.
- **Never delete a merged migration file.** If a table is dropped later, that drop is itself a new migration (`V00NN__drop_xxx.sql`).
- **Never renumber or reorder.** Version numbers are content-addressed by the order they landed, not by dependency or topic.

Rationale: multiple engineers and multiple environments replay this folder in order against their databases. Mutating history silently breaks any DB that has already applied the old version of a file.

## Execution today (temporary — until DB-02)

Until `Cena.Db.Migrator` (DB-02) ships:

- `src/actors/Cena.Actors/Services/PgVectorMigrationService.cs` runs at actor-host startup
- It loads every `V*.sql` file from this folder as an **embedded resource** and executes them in order against PostgreSQL
- All DDL is idempotent (`CREATE ... IF NOT EXISTS`) so re-running is safe
- There is **no migration tracking table yet** — PgVectorMigrationService is a bridge, not a real migrator

When DB-02 ships:

- The bridge is deleted
- `Cena.Db.Migrator` uses **DbUp** against a `cena.__migrations` tracking table
- Each `V*.sql` file is executed exactly once per database
- CI runs `Cena.Db.Migrator validate` on every PR and fails if the Marten config or the migration set has drifted

Until then, if you add a new `V*.sql`, it will run on every actor-host start — make sure your `IF NOT EXISTS` guards are correct.

## Scope

**In this folder**:

- pgvector extension enablement
- Custom tables Marten does not manage (e.g. `cena.content_embeddings`)
- Custom indexes, triggers, functions, roles
- Partitioning, materialized views, foreign tables
- Schema-level grants not covered by Marten

**NOT in this folder**:

- Marten event tables (`cena.mt_events`, `cena.mt_streams`) — auto-managed
- Marten document tables (`cena.mt_doc_*`) — generated from `opts.Schema.For<T>()` in MartenConfiguration
- Marten projection tables (`cena.mt_proj_*`) — generated from `opts.Projections.Add<T>()`

If you need a change to any Marten-managed table, edit the C# `MartenConfiguration.cs` — it will be picked up by Marten's own reconciliation logic. Do not try to express it as raw SQL here; the two will diverge.

## How to add a new migration

1. Find the highest `V{N}__*.sql` in this folder
2. Create `V{N+1}__your_description.sql` with your DDL
3. Use `IF NOT EXISTS` on every `CREATE` and `ALTER ... ADD COLUMN` statement
4. Verify it is standalone-executable: `psql -U cena -d cena_test -f db/migrations/V{N+1}__your_description.sql`
5. Commit the file in a dedicated PR with a description of why the change is needed
6. Do not edit existing migration files in the same PR

## Canonical reference

See also:

- [DB-00](../../docs/tasks/infra-db-migration/TASK-DB-00-pgvector-dimension-drift.md) — the original pgvector dimension drift fix
- [DB-01](../../docs/tasks/infra-db-migration/TASK-DB-01-unify-pgvector-ddl.md) — the task that created this folder and V0001
- [DB-02](../../docs/tasks/infra-db-migration/TASK-DB-02-migrator-project.md) — the real migrator project (future, will replace the PgVectorMigrationService bridge)
- [MartenConfiguration.cs](../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs) — Marten-managed schema
- [PgVectorMigrationService.cs](../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs) — the current temporary bridge that loads these files
