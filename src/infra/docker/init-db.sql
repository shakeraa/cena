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

-- =============================================================================
-- DB-08 Phase 1: Per-Role Statement Timeouts + Pool Isolation
-- Creates three dedicated Postgres roles with per-role timeout settings
-- for blast-radius isolation between student OLTP and admin workloads
-- =============================================================================

-- DEV ONLY: These passwords are for local Docker development only.
-- Production uses Kubernetes secrets with different credentials.
-- DO NOT use these passwords in production environments.

DO $$
BEGIN
    -- Student host role: hot path, strict timeouts, OLTP-shaped workload
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'cena_student') THEN
        CREATE ROLE cena_student LOGIN PASSWORD 'cena_student_dev_password';
    END IF;
    
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
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'cena_admin') THEN
        CREATE ROLE cena_admin LOGIN PASSWORD 'cena_admin_dev_password';
    END IF;
    
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
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'cena_migrator') THEN
        CREATE ROLE cena_migrator LOGIN PASSWORD 'cena_migrator_dev_password';
    END IF;
    
    GRANT ALL PRIVILEGES ON SCHEMA cena TO cena_migrator;
    GRANT CREATE ON DATABASE cena TO cena_migrator;
    GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA cena TO cena_migrator;
    GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA cena TO cena_migrator;
    ALTER DEFAULT PRIVILEGES IN SCHEMA cena
        GRANT ALL ON TABLES TO cena_migrator;
END
$$;
