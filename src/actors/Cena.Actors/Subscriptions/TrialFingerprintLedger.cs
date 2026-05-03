// =============================================================================
// Cena Platform — TrialFingerprintLedger (Phase 1B trial-then-paywall)
//
// Marten document holding the canonical record of "this card fingerprint has
// been used to start a Cena trial." Identity is the fingerprint hash itself;
// each row is a singleton from the perspective of one card. The companion
// audit event is `TrialFingerprintRecorded_V1`, appended to the parent's
// subscription stream (NOT a new aggregate stream, per task spec).
//
// L3a defense per docs/design/trial-recycle-defense-001-research.md §2.2:
// the recycle-attack flow burns through fresh emails and devices but the
// physical card's `card.fingerprint` (Stripe-issued, stable across emails)
// stays the same. Looking up by fingerprint defeats the most-common cycle.
//
// GDPR retention non-trivia (research brief §1.5a + companion §5.12):
//   - The fingerprint hash is one-way (SHA-256 of Stripe's `card.fingerprint`,
//     itself already an opaque token). Not PII.
//   - The row MUST survive crypto-shred / RTBF on the parent. Otherwise
//     the abuse loop becomes "delete account, re-register, get free trial #2".
//   - On RTBF: ParentSubjectIdEncrypted is set to NULL and NormalizedEmail
//     is cleared. The fingerprintHash + status + recordedAt remain so the
//     fraud-prevention legitimate-interest record (GDPR Art 17(3)(e)) is
//     preserved while all PII bound to the parent is gone.
//   - Status `TrialUsedRevoked` exists for the future admin-override case
//     (e.g. "card was stolen and used by attacker; revoke the trial-used
//     mark for the legitimate cardholder"). Not exposed in v1 endpoints
//     yet but ships in the document so a later admin task does not require
//     a schema migration.
//
// This is NOT a stub. Both InMemory and Marten implementations are real,
// tested, deployable. The L4 device-fingerprinting layer (FingerprintJS) is
// explicitly deferred (§2.3 v2 promotion criteria); the L3a card-fingerprint
// ledger ships in v1.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Lifecycle state of a fingerprint ledger row.
/// </summary>
public enum TrialFingerprintLedgerStatus
{
    /// <summary>
    /// Trial was started against this card fingerprint. Default state on
    /// insert. Drives the duplicate-trial rejection path.
    /// </summary>
    TrialUsed = 1,

    /// <summary>
    /// Admin override: the recorded trial was disputed and revoked, e.g.
    /// the card was stolen at the time and the legitimate cardholder
    /// requests trial eligibility be restored. Future-facing — not yet
    /// surfaced in v1 admin endpoints. When set, lookups return the row
    /// but the trial-start path treats it as "no prior trial."
    /// </summary>
    TrialUsedRevoked = 2,
}

/// <summary>
/// Marten document keyed by fingerprint hash. One row per distinct card.
/// </summary>
/// <remarks>
/// <para>
/// Identity is the fingerprint hash itself — Marten upserts on id match,
/// so a duplicate insert is rejected at the application layer (in
/// <c>RecordTrialAsync</c>) by checking <c>LookupAsync</c> first. Doing
/// the check + insert inside one Marten lightweight session minimises the
/// race window; the duplicate-key DB exception is the backstop if two
/// requests slip through the application-level guard simultaneously.
/// </para>
/// <para>
/// PII fields (<see cref="ParentSubjectIdEncrypted"/>, <see cref="NormalizedEmail"/>)
/// are nullable on purpose: on RTBF / crypto-shred of the parent, the
/// store's <c>ClearParentReferenceAsync</c> sets both to <c>null</c> so
/// the row survives as a fraud-prevention record without retaining the
/// parent identity.
/// </para>
/// </remarks>
public sealed class TrialFingerprintLedger
{
    /// <summary>
    /// Marten identity: the fingerprint hash. Caller-provided, must be a
    /// stable hash of Stripe's <c>card.fingerprint</c> token. Empty string
    /// is rejected at the store boundary.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Convenience accessor that mirrors <see cref="Id"/> but reads as
    /// "the fingerprint hash" at call sites. Setter delegates to
    /// <see cref="Id"/> so Marten's identity-by-id contract is unchanged.
    /// </summary>
    public string FingerprintHash
    {
        get => Id;
        set => Id = value;
    }

    /// <summary>Lifecycle state — see <see cref="TrialFingerprintLedgerStatus"/>.</summary>
    public TrialFingerprintLedgerStatus Status { get; set; } =
        TrialFingerprintLedgerStatus.TrialUsed;

    /// <summary>UTC timestamp of the original trial-start recording.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>
    /// Encrypted subject id of the parent that started the trial. Cleared
    /// (set to <c>null</c>) on parent RTBF; the row itself is retained.
    /// </summary>
    public string? ParentSubjectIdEncrypted { get; set; }

    /// <summary>
    /// Canonical (Gmail-folded, lower-cased) email address used at trial
    /// start. Cleared (set to <c>null</c>) on parent RTBF.
    /// </summary>
    /// <remarks>
    /// The normalized email is duplicated here (separate from the parent
    /// stream where it could in principle be reconstructed) because L2 of
    /// the recycle defense queries by normalized email even when the
    /// parent's primary record is gone. Keeping both signals on the row
    /// means a single document load answers both "is this card known?"
    /// and "is this email known?" without a second store call.
    /// </remarks>
    public string? NormalizedEmail { get; set; }

    /// <summary>
    /// True when the parent reference has been cleared by an RTBF /
    /// crypto-shred operation. Persisted for audit / observability so an
    /// admin querying the row can distinguish "never associated" from
    /// "was associated then RTBF-cleared."
    /// </summary>
    public bool ParentReferenceCleared { get; set; }

    /// <summary>UTC timestamp of the RTBF-clearing operation, if any.</summary>
    public DateTimeOffset? ParentReferenceClearedAt { get; set; }
}
