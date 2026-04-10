-- =============================================================================
-- Cena PostgreSQL initialization
--
-- Task: DB-01 — this file now only sets up the schema, extensions, and grants.
-- The content_embeddings table (and all other application tables / indexes)
-- are owned by db/migrations/V*.sql and applied at actor-host startup by
-- PgVectorMigrationService (temporarily — until DB-02 replaces it with
-- Cena.Db.Migrator).
--
-- History:
--   - SAI-06: original pgvector + embeddings inline
--   - DB-00:  reconciled dimension drift (384 → 1536, ivfflat → hnsw)
--   - DB-01:  moved embeddings table to db/migrations/V0001__*.sql
--   - DB-02 (future): Cena.Db.Migrator takes over schema reconciliation
--
-- This script runs once on fresh Docker container creation via
-- docker-entrypoint-initdb.d. Everything here must be idempotent (CREATE
-- IF NOT EXISTS) so it is safe to re-run on existing containers.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS cena;

-- Required by Marten for JSON operations
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
-- NOTE: plv8 extension disabled - not available in postgres:alpine image
-- CREATE EXTENSION IF NOT EXISTS "plv8";

-- pgvector extension (content_embeddings table itself is created by V0001)
CREATE EXTENSION IF NOT EXISTS vector;

-- Grant permissions on the cena schema
GRANT ALL ON SCHEMA cena TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON TABLES TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON SEQUENCES TO cena;
