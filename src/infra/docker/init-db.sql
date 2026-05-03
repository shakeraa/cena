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
