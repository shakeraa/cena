# TASK-DB-00: Fix pgvector Dimension Drift (384 vs 1536)

**Priority**: CRITICAL — silent data corruption risk
**Effort**: 0.5 days
**Depends on**: Nothing
**Track**: A
**Status**: Not Started

---

## You Are

A senior .NET + Postgres engineer who treats schema drift as a production incident waiting to happen. You verify that local dev and deployed environments declare the same shape for the same table, or you fail the build.

## The Problem

The `cena.content_embeddings` table is declared in **three different places with two different vector dimensions**:

| Location | Vector Dimension | Model Assumed |
|---|---|---|
| [src/infra/docker/init-db.sql:26](../../../src/infra/docker/init-db.sql#L26) | `vector(384)` | mE5-small (multilingual) |
| [scripts/sql/001_pgvector_embeddings.sql:14](../../../scripts/sql/001_pgvector_embeddings.sql#L14) | `vector(1536)` | text-embedding-3-small |
| [src/actors/Cena.Actors/Services/PgVectorMigrationService.cs:52](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs#L52) | `vector(1536)` | text-embedding-3-small |

In local dev (Docker) the table is 384-dim. In every deployed environment the hosted migration service forces it to 1536-dim. Any test that bootstraps a dev container and then runs the embedding pipeline produces silent failures (dimension mismatch on INSERT, broken similarity searches, or worst: garbage similarity scores if a column is mis-sized but still accepted).

Additionally, the Docker init creates an `ivfflat` index while the canonical script creates an `hnsw` index — the two environments will not behave the same under load.

## Your Task

1. Confirm the **correct dimension** is 1536 (text-embedding-3-small) by reading:
   - [EmbeddingService.cs](../../../src/actors/Cena.Actors/Services/EmbeddingService.cs)
   - [EmbeddingAdminService.cs](../../../src/api/Cena.Admin.Api/EmbeddingAdminService.cs)
   - Any OpenAI model configuration in `appsettings*.json`
2. Update [src/infra/docker/init-db.sql](../../../src/infra/docker/init-db.sql) so the embeddings table and index match the canonical 1536-dim + hnsw shape.
3. Verify no code paths assume 384-dim by grepping the codebase for `384`.
4. Document the decision at the top of `init-db.sql` with a reference to the ADR (once DB-02 lands).
5. On local machines with existing dev DBs, the table must be dropped and recreated — add a `DROP TABLE IF EXISTS cena.content_embeddings` at the top of the fixed init script **only** if it's safe for local dev (it is — init-db.sql only runs on fresh containers).

## Files You Must Touch

- [src/infra/docker/init-db.sql](../../../src/infra/docker/init-db.sql) — primary fix

## Files You Must Read First

- [scripts/sql/001_pgvector_embeddings.sql](../../../scripts/sql/001_pgvector_embeddings.sql) — canonical shape
- [src/actors/Cena.Actors/Services/PgVectorMigrationService.cs](../../../src/actors/Cena.Actors/Services/PgVectorMigrationService.cs) — runtime migration authority
- [src/actors/Cena.Actors/Services/EmbeddingService.cs](../../../src/actors/Cena.Actors/Services/EmbeddingService.cs) — consumer

## Acceptance Criteria

- [ ] `init-db.sql` declares `vector(1536)` for `cena.content_embeddings.embedding`.
- [ ] `init-db.sql` creates an `hnsw` index (matching prod), not `ivfflat`.
- [ ] All three DDL sources agree on dimension, index type, and column set.
- [ ] Grep for `384` across the repo returns no DB-related matches.
- [ ] A fresh `docker compose up` produces a table whose shape exactly matches what `PgVectorMigrationService` would produce.
- [ ] PR description notes that this is a prerequisite for DB-01 and DB-02.

## Out of Scope

- Consolidating the three DDL sources into one — that's DB-01.
- Introducing a real migration runner — that's DB-02.
- Dropping or renaming the `content_embeddings` table in deployed environments — no deployed environment is affected by this change.
