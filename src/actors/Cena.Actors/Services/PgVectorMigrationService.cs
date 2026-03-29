// =============================================================================
// Cena Platform -- pgvector Migration Service
// SAI-06: Runs at startup to ensure pgvector extension and content_embeddings
// table exist with 1536-dim vectors (text-embedding-3-small).
// Idempotent: safe to run repeatedly.
// =============================================================================

using Cena.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Actors.Services;

/// <summary>
/// Background service that ensures the pgvector extension and content_embeddings
/// table are created on application startup. All DDL statements are idempotent.
/// </summary>
public sealed class PgVectorMigrationService : IHostedService
{
    private readonly string _connectionString;
    private readonly ILogger<PgVectorMigrationService> _logger;

    public PgVectorMigrationService(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PgVectorMigrationService> logger)
    {
        _connectionString = CenaConnectionStrings.GetPostgres(configuration, environment);
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Step 1: Enable pgvector extension
            await ExecuteNonQueryAsync(conn, "CREATE EXTENSION IF NOT EXISTS vector", ct);
            _logger.LogInformation("SAI-06: pgvector extension enabled");

            // Step 2: Ensure cena schema exists (Marten normally creates this)
            await ExecuteNonQueryAsync(conn, "CREATE SCHEMA IF NOT EXISTS cena", ct);

            // Step 3: Create content_embeddings table (1536-dim for text-embedding-3-small)
            await ExecuteNonQueryAsync(conn,
                "CREATE TABLE IF NOT EXISTS cena.content_embeddings (" +
                "  id                UUID PRIMARY KEY DEFAULT gen_random_uuid()," +
                "  content_block_id  TEXT NOT NULL," +
                "  pipeline_item_id  TEXT," +
                "  embedding         vector(1536) NOT NULL," +
                "  concept_ids       TEXT[] NOT NULL DEFAULT '{}'," +
                "  language          TEXT NOT NULL DEFAULT 'he'," +
                "  subject           TEXT NOT NULL DEFAULT ''," +
                "  content_type      TEXT NOT NULL DEFAULT ''," +
                "  text_preview      TEXT NOT NULL DEFAULT ''," +
                "  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW())", ct);

            // Step 3b: Add pipeline_item_id column if table already existed without it
            await ExecuteNonQueryAsync(conn,
                "DO $$ BEGIN " +
                "ALTER TABLE cena.content_embeddings ADD COLUMN IF NOT EXISTS pipeline_item_id TEXT; " +
                "EXCEPTION WHEN duplicate_column THEN NULL; " +
                "END $$", ct);

            // Step 4: Create HNSW index (not IVFFlat) for fast cosine similarity
            // HNSW: no need to pre-define cluster count, better recall, supports incremental inserts
            await ExecuteNonQueryAsync(conn,
                "CREATE INDEX IF NOT EXISTS idx_content_embeddings_hnsw " +
                "ON cena.content_embeddings USING hnsw (embedding vector_cosine_ops) " +
                "WITH (m = 16, ef_construction = 64)", ct);

            // Step 5: Supporting indexes for filtered search
            await ExecuteNonQueryAsync(conn,
                "CREATE INDEX IF NOT EXISTS idx_content_embeddings_concept_ids " +
                "ON cena.content_embeddings USING GIN (concept_ids)", ct);

            await ExecuteNonQueryAsync(conn,
                "CREATE INDEX IF NOT EXISTS idx_content_embeddings_subject " +
                "ON cena.content_embeddings (subject)", ct);

            await ExecuteNonQueryAsync(conn,
                "CREATE INDEX IF NOT EXISTS idx_content_embeddings_language " +
                "ON cena.content_embeddings (language)", ct);

            await ExecuteNonQueryAsync(conn,
                "CREATE INDEX IF NOT EXISTS idx_content_embeddings_content_type " +
                "ON cena.content_embeddings (content_type)", ct);

            await ExecuteNonQueryAsync(conn,
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_content_embeddings_block_unique " +
                "ON cena.content_embeddings (content_block_id)", ct);

            _logger.LogInformation(
                "SAI-06: content_embeddings table and indexes created/verified");
        }
        catch (Exception ex)
        {
            // Non-fatal: the app can still run without pgvector (search falls back to empty)
            _logger.LogWarning(ex,
                "SAI-06: Failed to initialize pgvector. Embedding search will be unavailable. " +
                "Ensure pgvector extension is installed in PostgreSQL.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
