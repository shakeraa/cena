-- =============================================================================
-- Cena Platform — Migration V0001
-- pgvector extension + content_embeddings table (canonical)
--
-- Task: DB-01 — unify all pgvector DDL into one authoritative file.
-- History:
--   - DB-00 fixed dimension drift in src/infra/docker/init-db.sql
--   - DB-01 (this file) consolidates ALL pgvector DDL here
--   - DB-02 (future) introduces Cena.Db.Migrator which will execute this file
--     via DbUp + Marten reconciliation; for now PgVectorMigrationService
--     loads this file from disk at actor-host startup
--
-- Rules (append-only, see db/migrations/README.md):
--   - This file is NEVER edited after merge. To change the shape, add a
--     new V0002__*.sql migration.
--   - The file is executable standalone via `psql -f V0001__*.sql` against
--     a fresh database that already has the cena schema.
--   - Idempotent: every CREATE uses IF NOT EXISTS so re-running is safe.
--
-- Canonical shape: 1536-dim vectors (OpenAI text-embedding-3-small).
-- Consumer: src/actors/Cena.Actors/Services/EmbeddingService.cs
-- =============================================================================

-- pgvector extension (required for vector column type and HNSW index)
CREATE EXTENSION IF NOT EXISTS vector;

-- Content embeddings table: stores pre-computed embeddings for ContentBlockDocuments
-- Used for semantic search, deduplication, and RAG retrieval.
CREATE TABLE IF NOT EXISTS cena.content_embeddings (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    content_block_id  TEXT NOT NULL,
    pipeline_item_id  TEXT,
    embedding         vector(1536) NOT NULL,
    concept_ids       TEXT[] NOT NULL DEFAULT '{}',
    language          TEXT NOT NULL DEFAULT 'he',
    subject           TEXT NOT NULL DEFAULT '',
    content_type      TEXT NOT NULL DEFAULT '',
    text_preview      TEXT NOT NULL DEFAULT '',
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- HNSW index for fast approximate nearest neighbor search (cosine similarity).
-- HNSW vs IVFFlat: no need to pre-define cluster count, better recall,
-- supports incremental inserts without rebuild. Parameters m and ef_construction
-- are pgvector defaults; revisit when corpus grows past ~100K vectors.
CREATE INDEX IF NOT EXISTS idx_content_embeddings_hnsw
    ON cena.content_embeddings USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- GIN index for concept_ids array overlap queries (&&)
CREATE INDEX IF NOT EXISTS idx_content_embeddings_concept_ids
    ON cena.content_embeddings USING GIN (concept_ids);

-- B-tree indexes for common filter columns (subject, language, content_type)
CREATE INDEX IF NOT EXISTS idx_content_embeddings_subject
    ON cena.content_embeddings (subject);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_language
    ON cena.content_embeddings (language);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_content_type
    ON cena.content_embeddings (content_type);

-- Unique constraint: exactly one embedding per content block. This enforces
-- the ON CONFLICT (content_block_id) DO UPDATE upsert semantics used by
-- EmbeddingService.UpsertEmbeddingAsync.
CREATE UNIQUE INDEX IF NOT EXISTS idx_content_embeddings_block_unique
    ON cena.content_embeddings (content_block_id);
