// =============================================================================
// Cena Platform -- Postgres Subject Key Store (ADR-0038, prr-003b follow-up)
//
// Durable per-subject key store backed by `cena_subject_keys`. Schema in
// db/migrations/V0002__cena_subject_keys.sql. Tombstones are irreversible:
// once DeleteAsync flips encrypted_key to NULL, no future GetOrCreateAsync
// re-derives the key and all prior ciphertext becomes undecryptable —
// the ADR-0038 crypto-shred guarantee. Concurrency is enforced by Postgres
// via INSERT ... ON CONFLICT DO NOTHING (create) + a single UPDATE (delete).
// =============================================================================

using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Cena.Infrastructure.Compliance.KeyStore;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="ISubjectKeyStore"/>.
/// Tombstones are durable across process restarts.
/// </summary>
/// <remarks>
/// Deletion is irreversible: once <see cref="DeleteAsync"/> has run, the row
/// is permanently marked tombstoned and <see cref="GetOrCreateAsync"/>
/// returns <c>null</c> forever after. This is the crypto-shred contract
/// from ADR-0038 — do not add a "restore" API.
/// </remarks>
public sealed class PostgresSubjectKeyStore : ISubjectKeyStore
{
    // Single schema + table name — keep in sync with V0002__cena_subject_keys.sql.
    // Public so test assemblies can assert the exact SQL shape without a
    // live Postgres container; flipping any of these is a compliance regression.
    public const string Table = "cena.cena_subject_keys";

    public const string SqlSelect =
        "SELECT encrypted_key FROM " + Table + " WHERE subject_id = @id";

    // INSERT ... ON CONFLICT DO NOTHING so racing inserts collapse cleanly.
    // A concurrent DELETE that flipped encrypted_key to NULL is preserved
    // because the ON CONFLICT path does not overwrite.
    public const string SqlInsert =
        "INSERT INTO " + Table + " (subject_id, encrypted_key) VALUES (@id, @key) " +
        "ON CONFLICT (subject_id) DO NOTHING";

    // Irreversible tombstone. The WHERE clause refuses to resurrect an
    // already-tombstoned row (idempotent) but always stamps tombstoned_at
    // if a live key existed.
    public const string SqlDelete =
        "UPDATE " + Table + " SET encrypted_key = NULL, tombstoned_at = NOW() " +
        "WHERE subject_id = @id AND encrypted_key IS NOT NULL";

    public const string SqlExists =
        "SELECT 1 FROM " + Table + " WHERE subject_id = @id AND encrypted_key IS NOT NULL";

    public const string SqlListActive =
        "SELECT subject_id FROM " + Table + " WHERE encrypted_key IS NOT NULL ORDER BY subject_id";

    private readonly NpgsqlDataSource _dataSource;
    private readonly SubjectKeyDerivation _derivation;
    private readonly ILogger<PostgresSubjectKeyStore>? _logger;

    public PostgresSubjectKeyStore(
        NpgsqlDataSource dataSource,
        SubjectKeyDerivation derivation,
        ILogger<PostgresSubjectKeyStore>? logger = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _derivation = derivation ?? throw new ArgumentNullException(nameof(derivation));
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetOrCreateAsync(string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return null;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // First, try to read. A tombstoned row returns DBNull; a live row
        // returns the stored key; a missing row returns no rows.
        var existing = await ReadKeyAsync(conn, subjectId, ct).ConfigureAwait(false);
        if (existing is { Found: true })
        {
            // Row exists. Null payload means the subject has been tombstoned.
            return existing.Value.Key;
        }

        // Row is absent — derive and insert. ON CONFLICT DO NOTHING handles
        // the race where another caller or another pod inserted first. After
        // the insert attempt we re-read to get the authoritative stored value.
        var derived = _derivation.DeriveSubjectKey(subjectId);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = SqlInsert;
            cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Text) { Value = subjectId });
            cmd.Parameters.Add(new NpgsqlParameter("@key", NpgsqlDbType.Bytea) { Value = derived });
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var afterInsert = await ReadKeyAsync(conn, subjectId, ct).ConfigureAwait(false);
        return afterInsert is { Found: true } ? afterInsert.Value.Key : derived;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return false;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlExists;
        cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Text) { Value = subjectId });
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return false;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlDelete;
        cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Text) { Value = subjectId });
        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var existed = rowsAffected > 0;
        _logger?.LogInformation(
            "[SIEM] SubjectKeyTombstoned: hash={SubjectIdHash}, priorExisted={Existed}",
            HashSubjectForLog(subjectId), existed);
        return existed;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListActiveSubjectsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlListActive;
        await using var reader = await cmd.ExecuteReaderAsync(
            CommandBehavior.Default, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return reader.GetString(0);
        }
    }

    /// <summary>
    /// Short, non-reversible hash of the subject ID for audit logging.
    /// Never log the raw subject ID (ADR-0038 §"Audit trail").
    /// Mirrors <see cref="InMemorySubjectKeyStore.HashSubjectForLog"/>.
    /// </summary>
    public static string HashSubjectForLog(string subjectId)
        => InMemorySubjectKeyStore.HashSubjectForLog(subjectId);

    private static async Task<(bool Found, byte[]? Key)?> ReadKeyAsync(
        NpgsqlConnection conn, string subjectId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlSelect;
        cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Text) { Value = subjectId });
        await using var reader = await cmd.ExecuteReaderAsync(
            CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null; // no row
        }

        if (await reader.IsDBNullAsync(0, ct).ConfigureAwait(false))
        {
            return (true, null); // tombstoned row
        }

        var len = reader.GetBytes(0, 0, null, 0, 0);
        var buffer = new byte[len];
        reader.GetBytes(0, 0, buffer, 0, (int)len);
        return (true, buffer);
    }
}
