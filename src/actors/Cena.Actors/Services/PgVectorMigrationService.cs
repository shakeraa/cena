// =============================================================================
// Cena Platform -- pgvector Migration Service (TEMPORARY BRIDGE)
//
// Task history:
//   - SAI-06: original inline DDL version
//   - DB-00:  fixed dimension drift in init-db.sql to match this service
//   - DB-01:  moved ALL raw DDL out of this file into
//             db/migrations/V{N}__*.sql; this service now loads and executes
//             those files at actor-host startup as a temporary bridge
//   - DB-02:  WILL REPLACE this service entirely with Cena.Db.Migrator
//             (DbUp + Marten reconciliation + tracking table + CI drift gate)
//
// TODO: DB-02 — delete this service once Cena.Db.Migrator ships.
//
// Until DB-02 lands, every actor-host start will:
//   1. Open a connection via the shared NpgsqlDataSource
//   2. Ensure the 'cena' schema exists
//   3. Read every V*.sql file from db/migrations/ in ASCII order
//   4. Execute each file as a single batch (idempotent via IF NOT EXISTS)
//
// This is a no-tracking bridge: there is no cena.__migrations table yet.
// Re-running a file is safe because all DDL uses IF NOT EXISTS guards. When
// DB-02 ships, the tracking table takes over and this service is deleted.
// =============================================================================

using Cena.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Actors.Services;

/// <summary>
/// Temporary bridge service that applies raw SQL migrations from
/// <c>db/migrations/V*.sql</c> at actor-host startup. Replaced by
/// <c>Cena.Db.Migrator</c> when DB-02 ships.
/// </summary>
public sealed class PgVectorMigrationService : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgVectorMigrationService> _logger;

    public PgVectorMigrationService(
        NpgsqlDataSource dataSource,
        ILogger<PgVectorMigrationService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the db/migrations directory relative to the repo root.
    /// In a development checkout, walks up from <see cref="AppContext.BaseDirectory"/>
    /// until it finds a directory containing db/migrations. In a deployed container,
    /// the db/migrations tree is copied to the working directory at build time, or
    /// the <c>CENA_MIGRATIONS_DIR</c> env var overrides the lookup entirely.
    /// </summary>
    private static string FindMigrationsDirectory()
    {
        var envOverride = Environment.GetEnvironmentVariable("CENA_MIGRATIONS_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride) && Directory.Exists(envOverride))
            return envOverride;

        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(current, "db", "migrations");
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(current);
            if (parent is null)
                break;
            current = parent.FullName;
        }

        // Fallback: relative to current working directory
        return Path.Combine(Directory.GetCurrentDirectory(), "db", "migrations");
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var migrationsDir = FindMigrationsDirectory();
            if (!Directory.Exists(migrationsDir))
            {
                _logger.LogWarning(
                    "DB-01: migrations directory not found at {Path}. " +
                    "Set CENA_MIGRATIONS_DIR env var or ensure db/migrations exists " +
                    "in the working directory. Embedding search will be unavailable.",
                    migrationsDir);
                return;
            }

            var migrationFiles = Directory
                .EnumerateFiles(migrationsDir, "V*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                .ToList();

            if (migrationFiles.Count == 0)
            {
                _logger.LogWarning(
                    "DB-01: no V*.sql migration files found in {Path}",
                    migrationsDir);
                return;
            }

            _logger.LogInformation(
                "DB-01: found {Count} migration file(s) in {Path}",
                migrationFiles.Count, migrationsDir);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Ensure the cena schema exists before applying migrations that
            // reference cena.* tables. Marten normally creates this too, but
            // the migration service may run before Marten's first call.
            await ExecuteNonQueryAsync(conn, "CREATE SCHEMA IF NOT EXISTS cena", ct);

            foreach (var filePath in migrationFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var sql = await File.ReadAllTextAsync(filePath, ct);
                if (string.IsNullOrWhiteSpace(sql))
                {
                    _logger.LogWarning("DB-01: skipping empty migration {File}", fileName);
                    continue;
                }

                _logger.LogInformation("DB-01: applying migration {File}", fileName);
                await ExecuteNonQueryAsync(conn, sql, ct);
                _logger.LogInformation("DB-01: applied {File} successfully", fileName);
            }

            _logger.LogInformation(
                "DB-01: all {Count} migration(s) applied",
                migrationFiles.Count);
        }
        catch (Exception ex)
        {
            // Non-fatal: the app can still run without pgvector (search falls back to empty)
            _logger.LogWarning(ex,
                "DB-01: failed to apply pgvector migrations. Embedding search will be unavailable. " +
                "Ensure pgvector extension is installed and db/migrations/ is reachable.");
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
