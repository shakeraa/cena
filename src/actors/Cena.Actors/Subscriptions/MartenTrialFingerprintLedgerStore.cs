// =============================================================================
// Cena Platform — MartenTrialFingerprintLedgerStore (Phase 1B trial-then-paywall)
//
// Production implementation of ITrialFingerprintLedgerStore. Mirrors the
// MartenTrialAllotmentConfigStore pattern of "singleton document + audit
// event in one Marten lightweight session for atomicity":
//
//   1. Load the existing fingerprint row (if any) by id = fingerprintHash.
//   2. If TrialUsed → throw TrialAbuseException without writing anything.
//   3. Else load all rows that match the normalized email; if any are
//      TrialUsed → throw TrialAbuseException.
//   4. Otherwise: Store the new document AND Append a
//      TrialFingerprintRecorded_V1 event onto the parent's
//      `subscription-{parentSubjectIdEncrypted}` stream, then SaveChanges.
//
// Atomicity: steps (4) happen in one session, one SaveChangesAsync call.
// The duplicate-key DB exception is the backstop if two concurrent
// requests slip past the application-level guard simultaneously — Marten
// will reject the second insert and the caller surface translates that
// to the same TrialAbuseException("trial_already_used") shape.
//
// Stream identity convention: the audit event is appended to the SAME
// stream key as the parent's SubscriptionAggregate (subscription-{parent}).
// This means a single FetchStreamAsync on the parent's id returns the
// full lifecycle including this audit record. We do NOT call StartStream
// here — by the time a trial is being recorded the parent stream has
// always already been started by a SubscriptionActivated_V1 (or similar)
// upstream event in the trial-state-machine task. Append-only is correct.
//
// GDPR retention specifics (the row survives RTBF):
//   - ClearParentReferenceAsync issues a Marten patch on every matching
//     row, NULLing ParentSubjectIdEncrypted and NormalizedEmail and
//     setting ParentReferenceCleared = true. The fingerprintHash + status
//     + recordedAt are preserved so the legitimate-interest record
//     persists per GDPR Art 17(3)(e). This is what the
//     TrialFingerprintLedgerGdprTests exercise asserts.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten-backed <see cref="ITrialFingerprintLedgerStore"/>. Used in
/// production so the ledger survives pod restarts and is shared across
/// replicas.
/// </summary>
public sealed class MartenTrialFingerprintLedgerStore : ITrialFingerprintLedgerStore
{
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;

    public MartenTrialFingerprintLedgerStore(IDocumentStore store, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task RecordTrialAsync(
        string fingerprintHash,
        string parentSubjectIdEncrypted,
        string normalizedEmail,
        CancellationToken ct)
    {
        ValidateInputs(fingerprintHash, parentSubjectIdEncrypted, normalizedEmail);

        await using var session = _store.LightweightSession();

        // L3a: fingerprint check first — singleton load by id.
        var existingByFingerprint =
            await session.LoadAsync<TrialFingerprintLedger>(fingerprintHash, ct);
        if (existingByFingerprint is { Status: TrialFingerprintLedgerStatus.TrialUsed })
        {
            throw new TrialAbuseException(
                "trial_already_used",
                TrialAbuseSignal.FingerprintHashMatch);
        }

        // L2: normalized-email check — any active row with the same email.
        // Cleared rows have NormalizedEmail = null after RTBF; they don't
        // match by definition because the equality predicate excludes them.
        var emailMatches = await session
            .Query<TrialFingerprintLedger>()
            .Where(r => r.NormalizedEmail == normalizedEmail
                     && r.Status == TrialFingerprintLedgerStatus.TrialUsed)
            .ToListAsync(ct);
        if (emailMatches.Count > 0)
        {
            throw new TrialAbuseException(
                "trial_already_used",
                TrialAbuseSignal.NormalizedEmailMatch);
        }

        var now = _clock.GetUtcNow();
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
        session.Store(doc);

        // Audit event onto the parent's existing subscription stream. The
        // parent stream has always been started upstream (by trial-start
        // command) before this record happens, so Append (not StartStream)
        // is correct.
        var parentStreamKey = SubscriptionAggregate.StreamKey(parentSubjectIdEncrypted);
        var audit = new TrialFingerprintRecorded_V1(
            FingerprintHash: fingerprintHash,
            ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
            NormalizedEmail: normalizedEmail,
            RecordedAt: now);
        session.Events.Append(parentStreamKey, audit);

        await session.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<TrialFingerprintLedger?> LookupAsync(
        string fingerprintHash,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fingerprintHash)) return null;
        await using var session = _store.QuerySession();
        return await session.LoadAsync<TrialFingerprintLedger>(fingerprintHash, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TrialFingerprintLedger>> LookupByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return Array.Empty<TrialFingerprintLedger>();
        }
        await using var session = _store.QuerySession();
        var rows = await session
            .Query<TrialFingerprintLedger>()
            .Where(r => r.NormalizedEmail == normalizedEmail)
            .ToListAsync(ct);
        return rows.ToArray();
    }

    /// <inheritdoc/>
    public async Task<int> ClearParentReferenceAsync(
        string parentSubjectIdEncrypted,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Parent subject id is required.",
                nameof(parentSubjectIdEncrypted));
        }

        await using var session = _store.LightweightSession();

        // Snapshot the matching rows so we can compute the count to return
        // and so the patch operates on stable ids (avoiding LINQ-against-
        // mutating-store edge cases on the IDocumentSession surface).
        var matches = await session
            .Query<TrialFingerprintLedger>()
            .Where(r => r.ParentSubjectIdEncrypted == parentSubjectIdEncrypted)
            .ToListAsync(ct);

        var clearedAt = _clock.GetUtcNow();
        foreach (var row in matches)
        {
            row.ParentSubjectIdEncrypted = null;
            row.NormalizedEmail = null;
            row.ParentReferenceCleared = true;
            row.ParentReferenceClearedAt = clearedAt;
            session.Store(row);
        }

        await session.SaveChangesAsync(ct);
        return matches.Count;
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
}
