// =============================================================================
// Cena Platform -- Subject Key Store (ADR-0038, prr-003b)
//
// Per-subject key management for crypto-shredding. Each data subject has a
// 256-bit AES-GCM key derived via HKDF from a root key, bound to the
// subject's ID. Destroying the key = erasing the subject's PII ciphertext.
//
// Lifecycle:
//   - GetOrCreateAsync: derive the key on demand (idempotent until delete).
//   - ExistsAsync: check without creating — used by the decrypt path to
//     distinguish "not derived yet" (derive + return) from "erased".
//   - DeleteAsync: flip the tombstone. All future derivations refuse; all
//     ciphertext for this subject becomes undecryptable.
//
// After DeleteAsync, GetOrCreateAsync returns null and TryDecrypt in
// EncryptedFieldAccessor returns the [erased] sentinel per ADR-0038.
// =============================================================================

namespace Cena.Infrastructure.Compliance.KeyStore;

/// <summary>
/// Per-subject key store for crypto-shredding (ADR-0038).
/// </summary>
public interface ISubjectKeyStore
{
    /// <summary>
    /// Derive (or return cached) the AES-GCM 256-bit key for this subject.
    /// Returns <c>null</c> if the subject has been tombstoned — the caller
    /// must treat this as "erased" and decrypt to <c>ErasedSentinel.Value</c>.
    /// </summary>
    ValueTask<byte[]?> GetOrCreateAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Check whether a subject's key exists (i.e. is still derivable — not
    /// tombstoned). Does NOT create a row, so a subject with no events yet
    /// will return <c>false</c>.
    /// </summary>
    ValueTask<bool> ExistsAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Irreversibly tombstone the subject's key. Called by RetentionWorker
    /// (30-day window expired) and ErasureWorker (data-subject request).
    /// Returns <c>true</c> if a key was present before this call.
    /// </summary>
    ValueTask<bool> DeleteAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Enumerate subjects whose key has been materialised at least once
    /// (used by RetentionWorker to iterate candidates for 30-day expiry).
    /// Excludes tombstoned subjects.
    /// </summary>
    IAsyncEnumerable<string> ListActiveSubjectsAsync(CancellationToken ct = default);
}
