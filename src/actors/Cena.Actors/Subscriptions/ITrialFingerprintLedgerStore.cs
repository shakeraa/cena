// =============================================================================
// Cena Platform — ITrialFingerprintLedgerStore (Phase 1B trial-then-paywall)
//
// Persistence + duplicate-detection seam for the L3a card-fingerprint defense
// layer per docs/design/trial-recycle-defense-001-research.md §2.2.
//
// The store has two responsibilities and one GDPR carve-out:
//
//   1. Record(fingerprintHash, parentSubjectIdEncrypted, normalizedEmail)
//      writes the singleton row keyed by fingerprint hash, AND appends a
//      TrialFingerprintRecorded_V1 audit event to the parent's
//      `subscription-{parentSubjectIdEncrypted}` stream. Throws
//      TrialAbuseException("trial_already_used") if either the fingerprint
//      OR the normalized email already exists in TrialUsed state.
//
//   2. Lookup(fingerprintHash) and LookupByNormalizedEmail(email) return
//      the row(s) for read-side checks (admin status surface, future
//      override flow).
//
//   3. ClearParentReference(parentSubjectIdEncrypted) — the GDPR carve-out.
//      When the parent's subscription stream is crypto-shredded under
//      RTBF, this method scrubs the parent reference + email from any
//      ledger row that pointed at that parent. The fingerprintHash stays
//      so the fraud-prevention record persists per Art 17(3)(e).
//
// Matched implementations:
//   - InMemoryTrialFingerprintLedgerStore (production-grade for single-host)
//   - MartenTrialFingerprintLedgerStore   (multi-replica)
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Thrown when a trial-start request collides with an existing ledger row.
/// The <see cref="ReasonCode"/> is the structured error string surfaced in
/// the 409 response body per companion brief §5.7 ("trial-already-used").
/// </summary>
public sealed class TrialAbuseException : Exception
{
    /// <summary>Canonical reason code for the API surface.</summary>
    public string ReasonCode { get; }

    /// <summary>
    /// Optional context: which signal triggered the rejection — fingerprint
    /// match or normalized-email match. Useful for telemetry, never returned
    /// to the caller (would help an attacker iterate).
    /// </summary>
    public TrialAbuseSignal MatchedSignal { get; }

    public TrialAbuseException(string reasonCode, TrialAbuseSignal matchedSignal)
        : base(reasonCode)
    {
        ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
        MatchedSignal = matchedSignal;
    }
}

/// <summary>
/// Which recycle-defense signal flagged the duplicate. Internal-only —
/// used for telemetry + admin observability. NOT surfaced to the API so
/// an attacker cannot bisect which dimension to vary.
/// </summary>
public enum TrialAbuseSignal
{
    /// <summary>The fingerprint hash already exists in <c>TrialUsed</c> state.</summary>
    FingerprintHashMatch = 1,

    /// <summary>
    /// The normalized email matches an existing <c>TrialUsed</c> row's email.
    /// Catches the "Gmail-alias" attack (<c>alice+x@gmail.com</c> /
    /// <c>a.l.i.c.e@gmail.com</c>) before the fingerprint check fires.
    /// </summary>
    NormalizedEmailMatch = 2,
}

/// <summary>
/// Persistence + duplicate-detection seam for the trial fingerprint ledger.
/// </summary>
public interface ITrialFingerprintLedgerStore
{
    /// <summary>
    /// Record a new trial start. Writes the singleton ledger row keyed by
    /// fingerprint hash AND appends a <c>TrialFingerprintRecorded_V1</c>
    /// audit event to the parent's subscription stream.
    /// </summary>
    /// <param name="fingerprintHash">
    /// One-way hash of Stripe's <c>card.fingerprint</c>. Caller is responsible
    /// for hashing — the store treats it as opaque.
    /// </param>
    /// <param name="parentSubjectIdEncrypted">
    /// Encrypted subject id of the parent. Drives the parent stream key
    /// (<c>subscription-{parentSubjectIdEncrypted}</c>) for the audit event.
    /// </param>
    /// <param name="normalizedEmail">
    /// Canonical email per <see cref="EmailNormalizer.Normalize"/>. Caller
    /// must have already normalized; the store does NOT re-normalize so
    /// callers are free to pass an opaque token if they wish — but in
    /// production every call site uses <c>EmailNormalizer.Normalize</c> for
    /// consistency with the L2 defense layer.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TrialAbuseException">
    /// Thrown when the fingerprint OR normalized email is already present in
    /// <see cref="TrialFingerprintLedgerStatus.TrialUsed"/> state. Reason
    /// code is always <c>"trial_already_used"</c> — the matched signal is
    /// kept on the exception for telemetry but not exposed to API callers.
    /// </exception>
    Task RecordTrialAsync(
        string fingerprintHash,
        string parentSubjectIdEncrypted,
        string normalizedEmail,
        CancellationToken ct);

    /// <summary>
    /// Look up the ledger row by fingerprint hash. Returns <c>null</c> when
    /// no trial has been recorded against this card.
    /// </summary>
    Task<TrialFingerprintLedger?> LookupAsync(
        string fingerprintHash,
        CancellationToken ct);

    /// <summary>
    /// List all ledger rows whose <c>NormalizedEmail</c> matches. Returns
    /// the empty list when no trials have been recorded against this email.
    /// Multiple rows can exist when the same email-normalized form was used
    /// across multiple distinct card fingerprints (e.g. household-shared
    /// addresses; admin override re-records).
    /// </summary>
    Task<IReadOnlyList<TrialFingerprintLedger>> LookupByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken ct);

    /// <summary>
    /// GDPR right-to-be-forgotten / crypto-shred carve-out per docs/design/
    /// trial-recycle-defense-001-research.md §1.5a + companion brief §5.12.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a parent triggers RTBF on their account, the parent's subscription
    /// stream is crypto-shredded per ADR-0038. The fingerprint ledger row
    /// must SURVIVE this shred — otherwise the abuse loop becomes
    /// "delete account, re-register, claim new trial." The fingerprintHash
    /// itself is one-way and is not PII; the row remains as a fraud-
    /// prevention legitimate-interest record per GDPR Art 17(3)(e)
    /// ("processing necessary for the establishment, exercise or defence
    /// of legal claims").
    /// </para>
    /// <para>
    /// This method scrubs <see cref="TrialFingerprintLedger.ParentSubjectIdEncrypted"/>
    /// and <see cref="TrialFingerprintLedger.NormalizedEmail"/> — both are
    /// PII — and sets <see cref="TrialFingerprintLedger.ParentReferenceCleared"/>
    /// + <see cref="TrialFingerprintLedger.ParentReferenceClearedAt"/> for
    /// audit. The fingerprintHash, status, and recordedAt are preserved.
    /// </para>
    /// <para>
    /// Returns the number of rows scrubbed. Zero is a normal outcome when
    /// the parent never recorded a trial; the caller should not treat it
    /// as an error.
    /// </para>
    /// </remarks>
    Task<int> ClearParentReferenceAsync(
        string parentSubjectIdEncrypted,
        CancellationToken ct);
}
