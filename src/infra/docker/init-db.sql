-- Cena PostgreSQL initialization
-- Marten will auto-create its own tables (mt_events, mt_doc_*) on first run
-- This script sets up the schema and extensions

CREATE SCHEMA IF NOT EXISTS cena;

-- Required by Marten for JSON operations
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "plv8";

-- Grant permissions
GRANT ALL ON SCHEMA cena TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON TABLES TO cena;
ALTER DEFAULT PRIVILEGES IN SCHEMA cena GRANT ALL ON SEQUENCES TO cena;
