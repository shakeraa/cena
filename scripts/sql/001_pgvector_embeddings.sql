-- =============================================================================
-- Cena Platform -- SAI-06: pgvector Content Embeddings
-- Enables the pgvector extension and creates the content_embeddings table
-- for semantic search and deduplication via cosine similarity.
-- =============================================================================

-- Enable pgvector extension (requires PostgreSQL superuser or rds_superuser)
CREATE EXTENSION IF NOT EXISTS vector;

-- Content embeddings table: stores pre-computed embeddings for ContentBlockDocuments
CREATE TABLE IF NOT EXISTS cena.content_embeddings (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    content_block_id  TEXT NOT NULL,
    embedding         vector(1536) NOT NULL,
    concept_ids       TEXT[] NOT NULL DEFAULT '{}',
    language          TEXT NOT NULL DEFAULT 'he',
    subject           TEXT NOT NULL DEFAULT '',
    content_type      TEXT NOT NULL DEFAULT '',
    text_preview      TEXT NOT NULL DEFAULT '',
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- HNSW index for fast approximate nearest neighbor search (cosine similarity)
CREATE INDEX IF NOT EXISTS idx_content_embeddings_hnsw
    ON cena.content_embeddings USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- GIN index on concept_ids array for concept-filtered queries
CREATE INDEX IF NOT EXISTS idx_content_embeddings_concept_ids
    ON cena.content_embeddings USING GIN (concept_ids);

-- B-tree index on subject for subject-scoped queries
CREATE INDEX IF NOT EXISTS idx_content_embeddings_subject
    ON cena.content_embeddings (subject);

-- B-tree index on language for language-scoped queries
CREATE INDEX IF NOT EXISTS idx_content_embeddings_language
    ON cena.content_embeddings (language);

-- Unique constraint: one embedding per content block
CREATE UNIQUE INDEX IF NOT EXISTS idx_content_embeddings_block_unique
    ON cena.content_embeddings (content_block_id);
