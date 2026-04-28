// =============================================================================
// Cena Platform — InMemoryTrialFingerprintLedgerStore (Phase 1B trial-then-paywall)
//
// Production-grade for single-host installs (matches the ADR-0042 InMemory-
// is-v1 convention used by the rest of the Subscriptions bounded context).
// Multi-replica deployments use MartenTrialFingerprintLedgerStore.
//
// State carried:
//   - _byFingerprint : the canonical singleton-per-fingerprint store
//   - _eventsByParent: per-parent audit stream of TrialFingerprintRecorded_V1
//                      events. Mirrors the Marten pattern of appending to
//                      `subscription-{parentSubjectIdEncrypted}` so callers
//                      see the same observable behavior across hosts.
//
// Concurrency: a single _gate lock around all operations. The hot path is
// Lookup (already a dictionary read; the lock is uncontended in the common
// case). The InMemory variant is not optimised for high write throughput —
// production multi-replica boxes use Marten which has its own concurrency
// model.
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// In-memory <see cref="ITrialFingerprintLedgerStore"/>. Production-grade
/// for single-host installs.
/// </summary>
public sealed class InMemoryTrialFingerprintLedgerStore : ITrialFingerprintLedgerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TrialFingerprintLedger> _byFingerprint =
        new(StringComparer.Ordinal);

    // Per-parent audit stream — mirrors the Marten pattern of appending to
    // `subscription-{parentSubjectIdEncrypted}`. Stored unkeyed by stream
    // prefix here; the test surface (and any in-process consumer) pulls
    // by parent id directly via ReadAuditEventsAsync below.
    private readonly Dictionary<string, List<TrialFingerprintRecorded_V1>> _eventsByParent =
        new(StringComparer.Ordinal);

    private readonly TimeProvider _clock;

    public InMemoryTrialFingerprintLedgerStore() : this(TimeProvider.System) { }

    public InMemoryTrialFingerprintLedgerStore(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public Task RecordTrialAsync(
        string fingerprintHash,
        string parentSubjectIdEncrypted,
        string normalizedEmail,
        CancellationToken ct)
    {
        ValidateInputs(fingerprintHash, parentSubjectIdEncrypted, normalizedEmail);

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            // L3a (fingerprint) check first: the strongest signal. Reject if
            // any TrialUsed row exists for this fingerprint. TrialUsedRevoked
            // means an admin override has cleared the prior block, so the
            // current request is allowed to proceed (and overwrites the row).
            if (_byFingerprint.TryGetValue(fingerprintHash, out var existing)
                && existing.Status == TrialFingerprintLedgerStatus.TrialUsed)
            {
                throw new TrialAbuseException(
                    "trial_already_used",
                    TrialAbuseSignal.FingerprintHashMatch);
            }

            // L2 (normalized-email) check: any active row whose NormalizedEmail
            // matches blocks. Cleared rows (NormalizedEmail = null after RTBF)
            // do not match because LINQ skips nulls; that is the intentional
            // post-RTBF behaviour — the email is no longer associated.
            foreach (var row in _byFingerprint.Values)
            {
                if (row.Status != TrialFingerprintLedgerStatus.TrialUsed) continue;
                if (string.Equals(row.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
                {
                    throw new TrialAbuseException(
                        "trial_already_used",
                        TrialAbuseSignal.NormalizedEmailMatch);
                }
            }

            var doc = new TrialFingerprintLedger
            {
                Id = fingerprintHash,
                Status = TrialFingerprintLedgerStatus.TrialUsed,
                RecordedAt = now,
                ParentSubjectIdEncrypted = parentSubjectIdEncrypted,
                NormalizedEmail = normalizedEmail,
                ParentReferenceCleared = false,
                ParentReferenceClearedAt = null,
            };
            _byFingerprint[fingerprintHash] = doc;

            var audit = new TrialFingerprintRecorded_V1(
                FingerprintHash: fingerprintHash,
                ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
                NormalizedEmail: normalizedEmail,
                RecordedAt: now);
            if (!_eventsByParent.TryGetValue(parentSubjectIdEncrypted, out var stream))
            {
                stream = new List<TrialFingerprintRecorded_V1>();
                _eventsByParent[parentSubjectIdEncrypted] = stream;
            }
            stream.Add(audit);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<TrialFingerprintLedger?> LookupAsync(
        string fingerprintHash,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fingerprintHash))
        {
            return Task.FromResult<TrialFingerprintLedger?>(null);
        }
        lock (_gate)
        {
            return Task.FromResult(
                _byFingerprint.TryGetValue(fingerprintHash, out var row) ? Clone(row) : null);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrialFingerprintLedger>> LookupByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return Task.FromResult<IReadOnlyList<TrialFingerprintLedger>>(
                Array.Empty<TrialFingerprintLedger>());
        }
        lock (_gate)
        {
            var matches = _byFingerprint.Values
                .Where(r => string.Equals(r.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
                .Select(Clone)
                .ToArray();
            return Task.FromResult<IReadOnlyList<TrialFingerprintLedger>>(matches);
        }
    }

    /// <inheritdoc/>
    public Task<int> ClearParentReferenceAsync(
        string parentSubjectIdEncrypted,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Parent subject id is required.",
                nameof(parentSubjectIdEncrypted));
        }
        var clearedAt = _clock.GetUtcNow();
        var count = 0;
        lock (_gate)
        {
            foreach (var row in _byFingerprint.Values)
            {
                if (string.Equals(row.ParentSubjectIdEncrypted, parentSubjectIdEncrypted,
                        StringComparison.Ordinal))
                {
                    row.ParentSubjectIdEncrypted = null;
                    row.NormalizedEmail = null;
                    row.ParentReferenceCleared = true;
                    row.ParentReferenceClearedAt = clearedAt;
                    count++;
                }
            }
            // The parent's audit-event stream is shredded with the rest of
            // the parent's subscription stream by the regular crypto-shred
            // path (ADR-0038); the in-memory mirror does the same here so
            // the observable behaviour matches the Marten variant.
            _eventsByParent.Remove(parentSubjectIdEncrypted);
        }
        return Task.FromResult(count);
    }

    /// <summary>
    /// Test-surface accessor — returns the audit events appended for a
    /// given parent. NOT part of the interface; consumed by InMemory
    /// contract tests + the GDPR test that asserts events vanish on
    /// crypto-shred.
    /// </summary>
    public IReadOnlyList<TrialFingerprintRecorded_V1> ReadAuditEventsForParent(
        string parentSubjectIdEncrypted)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            return Array.Empty<TrialFingerprintRecorded_V1>();
        }
        lock (_gate)
        {
            if (!_eventsByParent.TryGetValue(parentSubjectIdEncrypted, out var list))
            {
                return Array.Empty<TrialFingerprintRecorded_V1>();
            }
            return list.ToArray();
        }
    }

    private static void ValidateInputs(
        string fingerprintHash,
        string parentSubjectIdEncrypted,
        string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(fingerprintHash))
        {
            throw new ArgumentException(
                "Fingerprint hash is required.", nameof(fingerprintHash));
        }
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Parent subject id is required.", nameof(parentSubjectIdEncrypted));
        }
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException(
                "Normalized email is required.", nameof(normalizedEmail));
        }
    }

    private static TrialFingerprintLedger Clone(TrialFingerprintLedger src) => new()
    {
        Id = src.Id,
        Status = src.Status,
        RecordedAt = src.RecordedAt,
        ParentSubjectIdEncrypted = src.ParentSubjectIdEncrypted,
        NormalizedEmail = src.NormalizedEmail,
        ParentReferenceCleared = src.ParentReferenceCleared,
        ParentReferenceClearedAt = src.ParentReferenceClearedAt,
    };
}
