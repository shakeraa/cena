-- =============================================================================
-- Cena PostgreSQL initialization
-- Task: DB-00 — canonical shape unified with PgVectorMigrationService.cs;
--               see docs/tasks/infra-db-migration/TASK-DB-00-pgvector-dimension-drift.md
--               and TASK-DB-01-unify-pgvector-ddl.md.
--
-- This script runs once on fresh Docker container creation. It sets up the
-- cena schema, extensions, grants, and creates cena.content_embeddings with
-- the exact shape produced by
-- src/actors/Cena.Actors/Services/PgVectorMigrationService.cs at runtime.
-- Keeping these two sources in lockstep guarantees local-dev parity with
-- deployed environments.
--
-- DB-01 (follow-on) moves this DDL into db/migrations/V0001__*.sql and
-- removes the table definition from this file entirely, leaving only the
-- schema + extensions + grants. Until DB-01 ships, this file is the local
-- dev source of truth.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS cena;

-- Required by Marten for JSON operations
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
-- NOTE: plv8 extension disabled - not available in postgres:alpine image
-- CREATE EXTENSION IF NOT EXISTS "plv8";

-- SAI-06: pgvector extension for embedding-based semantic search
CREATE EXTENSION IF NOT EXISTS vector;

-- Grant permissions
GRANT ALL ON SCHEMA cena TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON TABLES TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON SEQUENCES TO cena;

-- =============================================================================
-- SAI-06: Content embeddings table for semantic search and deduplication.
-- Canonical shape: 1536-dim vectors (OpenAI text-embedding-3-small).
-- This table definition MUST match PgVectorMigrationService.StartAsync exactly.
-- If you edit one, edit both (or land DB-01 which consolidates them).
-- =============================================================================
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
-- HNSW (vs IVFFlat): no need to pre-define cluster count, better recall,
-- supports incremental inserts without rebuild. Parameters m and ef_construction
-- match PgVectorMigrationService exactly.
CREATE INDEX IF NOT EXISTS idx_content_embeddings_hnsw
    ON cena.content_embeddings USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- GIN index for concept_ids array overlap queries (&&)
CREATE INDEX IF NOT EXISTS idx_content_embeddings_concept_ids
    ON cena.content_embeddings USING GIN (concept_ids);

-- B-tree indexes for common filter columns
CREATE INDEX IF NOT EXISTS idx_content_embeddings_subject
    ON cena.content_embeddings (subject);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_language
    ON cena.content_embeddings (language);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_content_type
    ON cena.content_embeddings (content_type);

-- Unique constraint: exactly one embedding per content block (enforces
-- the ON CONFLICT (content_block_id) upsert semantics in EmbeddingService)
CREATE UNIQUE INDEX IF NOT EXISTS idx_content_embeddings_block_unique
    ON cena.content_embeddings (content_block_id);
