# TASK-DB-01: Unify pgvector DDL Into One Authoritative Location

**Priority**: HIGH — blocks DB-02
**Effort**: 1 day
**Depends on**: DB-00
**Track**: A
**Status**: Not Started

---

## You Are

A senior platform engineer who believes every piece of schema has exactly one owner. When you find three files that all claim to define the same table, you pick one, delete the others, and make sure the code that used to touch them is re-pointed at the survivor.

## The Problem

After DB-00 lands, `cena.content_embeddings` has consistent shape but is still defined in three places:

1. [src/infra/docker/init-db.sql](../../../src/infra/docker/init-db.sql) — runs once on local Docker container creation
2. [scripts/sql/001_pgvector_embeddings.sql](../../../scripts/sql/001_pgvector_embeddings.sql) — reference script, not actually executed anywhere
3. [src/actors/Cena.Actors/Services/PgVectorMigrationService.cs](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs) — inline C# string DDL run at every host start

This guarantees future drift every time someone updates one and forgets the others.

## Your Task

1. Pick the canonical home: **raw SQL under a versioned migrations folder**. Recommended layout:
   ```
   db/migrations/V0001__pgvector_extension_and_embeddings.sql
   ```
2. Move the canonical DDL (1536-dim, hnsw, all indexes, `pipeline_item_id` column, the `ContentEmbeddingDeprecation` step that's in the C# today — whatever is authoritative) into `V0001__pgvector_extension_and_embeddings.sql`.
3. Delete [scripts/sql/001_pgvector_embeddings.sql](../../../scripts/sql/001_pgvector_embeddings.sql) — it was a reference document, now superseded.
4. Strip the inline DDL from `PgVectorMigrationService.cs`. The service itself can stay as a **temporary** bridge: it reads `db/migrations/V0001__*.sql` from disk and executes it idempotently at startup. It does NOT duplicate the SQL inside the C# file. Add a `// TODO: DB-02 — delete this service once Cena.Db.Migrator ships`.
5. Update [src/infra/docker/init-db.sql](../../../src/infra/docker/init-db.sql) so it only does what a fresh local container actually needs: create schema, grants, extensions. Remove the embeddings table definition from this file — in local dev the hosted migration service will create it on first host start, same as every other environment. This removes divergence between local and deployed shapes forever.
6. Add a README at `db/migrations/README.md` explaining the naming convention (`V{4-digit}__{snake_description}.sql`), that files are append-only and never edited after merge, and that DB-02 will introduce the real runner.

## Files You Must Touch

- **Create**: `db/migrations/V0001__pgvector_extension_and_embeddings.sql`
- **Create**: `db/migrations/README.md`
- **Modify**: [src/actors/Cena.Actors/Services/PgVectorMigrationService.cs](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs) — load SQL from file, not inline
- **Modify**: [src/infra/docker/init-db.sql](../../../src/infra/docker/init-db.sql) — remove table/index definitions
- **Delete**: [scripts/sql/001_pgvector_embeddings.sql](../../../scripts/sql/001_pgvector_embeddings.sql)

## Files You Must Read First

- All three DDL sources listed in DB-00 to confirm there are no per-environment divergences beyond the ones already identified
- [src/actors/Cena.Actors.Host/Program.cs](../../../src/actors/Cena.Actors.Host/Program.cs) — to understand where the hosted service is registered

## Acceptance Criteria

- [ ] `db/migrations/V0001__pgvector_extension_and_embeddings.sql` exists and is the only place the embeddings table is defined in SQL.
- [ ] `PgVectorMigrationService.cs` reads the file from disk (embedded resource or content file) and runs it; no raw DDL lives inside the C# class.
- [ ] `init-db.sql` no longer defines `content_embeddings`.
- [ ] `scripts/sql/001_pgvector_embeddings.sql` is deleted.
- [ ] `db/migrations/README.md` documents the naming convention and append-only rule.
- [ ] Fresh `docker compose up` + host start produces an identical table shape to pre-change prod.
- [ ] No broken references (grep for the deleted file path returns nothing).
- [ ] PR links DB-02 as the next step that will remove `PgVectorMigrationService` entirely.

## Out of Scope

- Replacing `PgVectorMigrationService` with a real migration runner (DB-02).
- Adding migration tracking to `cena.__migrations` (DB-02).
- Schema evolution for anything other than the embeddings table.
