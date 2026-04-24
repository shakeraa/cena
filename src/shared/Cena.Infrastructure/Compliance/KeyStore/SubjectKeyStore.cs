// =============================================================================
// Cena Platform -- In-Memory Subject Key Store (ADR-0038, prr-003b)
//
// In-memory implementation of ISubjectKeyStore backed by a thread-safe map.
// Tombstones are persistent within the process lifetime — crash-restart
// resets the "alive" set (keys re-derive from the root), but the
// "tombstoned" set is lost unless written somewhere durable.
//
// PRODUCTION BACKING IS OUT OF SCOPE FOR prr-003b (time-boxed): a separate
// PostgreSQL-backed implementation will be added under a follow-up ticket.
// Until then, the compliance health-check fails if the process is
// configured as production-grade (dev fallback detected OR no durable
// store configured). See ADR-0038 §"Storage" — durable tombstones are the
// one non-deferrable persistence requirement.
//
// Concurrency contract:
//   - GetOrCreateAsync and DeleteAsync are linearizable via ConcurrentDictionary.
//   - A DeleteAsync that races with a concurrent GetOrCreateAsync either
//     a) lands before the Get → Get sees the tombstone → returns null, or
//     b) lands after the Get → the caller has a live byte[] but subsequent
//        Gets return null. The single in-flight encrypt/decrypt using the
//        pre-tombstone byte[] is acceptable per ADR-0038 read-path contract.
// =============================================================================

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance.KeyStore;

/// <summary>
/// In-memory implementation of <see cref="ISubjectKeyStore"/>.
/// Thread-safe. Tombstones are process-lifetime only (see class remarks).
/// </summary>
public sealed class InMemorySubjectKeyStore : ISubjectKeyStore
{
    private static readonly byte[] TombstoneMarker = Array.Empty<byte>();

    private readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly SubjectKeyDerivation _derivation;
    private readonly ILogger<InMemorySubjectKeyStore>? _logger;

    public InMemorySubjectKeyStore(SubjectKeyDerivation derivation, ILogger<InMemorySubjectKeyStore>? logger = null)
    {
        _derivation = derivation ?? throw new ArgumentNullException(nameof(derivation));
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<byte[]?> GetOrCreateAsync(string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        var value = _keys.GetOrAdd(subjectId, id => _derivation.DeriveSubjectKey(id));
        // Tombstone marker is a zero-length byte[] — the derivation always
        // returns 32 bytes, so zero-length unambiguously means "erased".
        if (value.Length == 0)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }
        return ValueTask.FromResult<byte[]?>(value);
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return ValueTask.FromResult(false);
        }
        if (!_keys.TryGetValue(subjectId, out var value))
        {
            return ValueTask.FromResult(false);
        }
        return ValueTask.FromResult(value.Length != 0);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return ValueTask.FromResult(false);
        }

        var existed = _keys.TryGetValue(subjectId, out var prior) && prior.Length != 0;
        // Overwrite to the tombstone marker. GetOrAdd after this returns the
        // tombstone, so subsequent GetOrCreateAsync returns null.
        _keys[subjectId] = TombstoneMarker;

        _logger?.LogInformation(
            "[SIEM] SubjectKeyTombstoned: hash={SubjectIdHash}, priorExisted={Existed}",
            HashSubjectForLog(subjectId), existed);

        return ValueTask.FromResult(existed);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListActiveSubjectsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var kv in _keys)
        {
            ct.ThrowIfCancellationRequested();
            if (kv.Value.Length != 0)
            {
                yield return kv.Key;
            }
        }
        // async state-machine happy: a no-op await keeps the signature honest
        await Task.CompletedTask;
    }

    /// <summary>
    /// Short, non-reversible hash of the subject ID for audit logging.
    /// Never log the raw subject ID in compliance events (ADR-0038 §"Audit trail").
    /// </summary>
    public static string HashSubjectForLog(string subjectId)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(subjectId);
        var hash = sha.ComputeHash(bytes);
        // 16 hex chars = 64 bits of entropy — plenty for audit correlation.
        return Convert.ToHexString(hash, 0, 8);
    }
}
