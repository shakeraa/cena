-- =============================================================================
-- Cena Platform — Migration V0002
-- cena_subject_keys table for PostgresSubjectKeyStore (ADR-0038, prr-003b)
--
-- Task: prr-003b follow-up — durable tombstone backing for crypto-shredding.
-- Consumer: src/shared/Cena.Infrastructure/Compliance/KeyStore/PostgresSubjectKeyStore.cs
--
-- Rules (append-only, see db/migrations/README.md):
--   - This file is NEVER edited after merge. To change the shape, add a
--     new V{N}__*.sql migration.
--   - Standalone-executable: `psql -f V0002__*.sql` against a fresh database
--     that already has the `cena` schema.
--   - Idempotent: every CREATE uses IF NOT EXISTS so re-running is safe.
--
-- Semantics:
--   - subject_id       : opaque subject identifier (student ID, consent subject, etc.)
--   - encrypted_key    : materialized AES-GCM-256 key bytes (32 bytes) or NULL
--                        NULL means the subject has been tombstoned — the row
--                        stays around as a permanent refusal to re-derive.
--   - tombstoned_at    : timestamp the tombstone flip happened (NULL while live)
--   - created_at       : first-derive timestamp for observability only
--
-- A tombstone is IRREVERSIBLE. There is no restore path. Any ciphertext
-- previously written for this subject becomes undecryptable (returns the
-- ErasedSentinel via EncryptedFieldAccessor). This is the crypto-shred
-- contract from ADR-0038.
-- =============================================================================

CREATE TABLE IF NOT EXISTS cena.cena_subject_keys (
    subject_id     TEXT PRIMARY KEY,
    encrypted_key  BYTEA NULL,
    tombstoned_at  TIMESTAMPTZ NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Partial index over live subjects only — used by
-- ListActiveSubjectsAsync (RetentionWorker candidate enumeration).
-- Skipping tombstoned rows keeps the index small as subjects are erased.
CREATE INDEX IF NOT EXISTS idx_cena_subject_keys_active
    ON cena.cena_subject_keys (subject_id)
    WHERE encrypted_key IS NOT NULL;

-- Index over tombstoned rows for compliance audits / retention reporting.
CREATE INDEX IF NOT EXISTS idx_cena_subject_keys_tombstoned_at
    ON cena.cena_subject_keys (tombstoned_at)
    WHERE tombstoned_at IS NOT NULL;
