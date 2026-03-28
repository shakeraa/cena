-- Cena PostgreSQL initialization
-- Marten will auto-create its own tables (mt_events, mt_doc_*) on first run
-- This script sets up the schema and extensions

CREATE SCHEMA IF NOT EXISTS cena;

-- Required by Marten for JSON operations
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
-- NOTE: plv8 extension disabled - not available in postgres:alpine image
-- CREATE EXTENSION IF NOT EXISTS "plv8";

-- SAI-008: pgvector extension for embedding-based semantic search
CREATE EXTENSION IF NOT EXISTS vector;

-- Grant permissions
GRANT ALL ON SCHEMA cena TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON TABLES TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON SEQUENCES TO cena;

-- SAI-008: Content embeddings table for semantic search and deduplication
-- Uses 384-dim vectors (mE5-small multilingual model)
CREATE TABLE IF NOT EXISTS cena.content_embeddings (
    content_block_id  TEXT PRIMARY KEY,
    embedding         vector(384) NOT NULL,
    content_type      TEXT NOT NULL DEFAULT '',
    subject           TEXT NOT NULL DEFAULT '',
    topic             TEXT NOT NULL DEFAULT '',
    concept_ids       TEXT[] NOT NULL DEFAULT '{}',
    language          TEXT NOT NULL DEFAULT 'he',
    text_preview      TEXT NOT NULL DEFAULT '',
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- IVFFlat index for fast cosine-similarity search on 10K+ items
CREATE INDEX IF NOT EXISTS idx_content_embeddings_vector
    ON cena.content_embeddings USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- GIN index for concept_ids array overlap queries
CREATE INDEX IF NOT EXISTS idx_content_embeddings_concept_ids
    ON cena.content_embeddings USING GIN (concept_ids);

-- B-tree indexes for common filter columns
CREATE INDEX IF NOT EXISTS idx_content_embeddings_subject
    ON cena.content_embeddings (subject);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_language
    ON cena.content_embeddings (language);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_content_type
    ON cena.content_embeddings (content_type);
