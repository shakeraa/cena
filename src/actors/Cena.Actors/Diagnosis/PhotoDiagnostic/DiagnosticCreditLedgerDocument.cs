// =============================================================================
// Cena Platform — DiagnosticCreditLedgerDocument (EPIC-PRR-J PRR-391)
//
// Append-only ledger entry that records one issued credit for a student
// whose photo-diagnostic dispute was confirmed as a real system error.
// Paired with the dispute aggregate (DiagnosticDisputeDocument) through
// DisputeId, which is the idempotency key: at most one ledger row per
// dispute.
//
// Why a ledger and not a counter-decrement:
//   - IPhotoDiagnosticMonthlyUsage is a monotonic "uploads attempted"
//     counter. Decrementing it would lie about history (metrics, audit
//     trails, and accuracy-audit samplers rely on it as a write-once
//     record of real invocations).
//   - The ledger instead records the intent "this student was granted
//     N free diagnostics for month M". PhotoDiagnosticQuotaGate reads
//     it at check-time and subtracts credits from the raw count before
//     the per-tier cap enforcer makes its decision. That keeps the
//     upload counter truthful AND lets the cap behave as if the error
//     never happened.
//   - Persisting the credit separately also gives support/finance a
//     straight audit: "who issued what to whom and why", surfaced in
//     PRR-390 admin dashboard without re-deriving from a counter diff.
//
// Data handling: StudentSubjectIdHash is already hashed upstream (matches
// DiagnosticDisputeDocument). CreditedBy is the admin subject id (not
// hashed — admins are staff, not students). Reason is free-text bounded
// at 500 chars. Retention is open-ended: credit history is financial
// audit and travels with the subscription lifecycle, not the 90-day
// dispute retention.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Categorical kinds of credit that can be issued on a confirmed dispute.
/// Currently just FreeUploadQuota; future kinds (ProRataRefund, etc.) can
/// be appended without schema migration because the enum serialises by
/// name via Marten's default JSON config.
/// </summary>
public enum DiagnosticCreditKind
{
    /// <summary>Free bump to the student's monthly upload cap.</summary>
    FreeUploadQuota,
}

/// <summary>
/// Marten doc: one row per credit issuance. Primary key is a GUID so the
/// same dispute cannot accidentally produce two ledger rows if the guard
/// in DiagnosticCreditService is bypassed — the uniqueness invariant is
/// enforced by the DisputeId index plus the "already Upheld" guard.
/// </summary>
public sealed record DiagnosticCreditLedgerDocument
{
    /// <summary>Ledger-row primary key (GUID, "D" format).</summary>
    public string Id { get; init; } = "";

    /// <summary>Dispute this credit resolves. One credit per dispute.</summary>
    public string DisputeId { get; init; } = "";

    /// <summary>
    /// Student the credit belongs to (hashed subject id — matches
    /// DiagnosticDisputeDocument.StudentSubjectIdHash).
    /// </summary>
    public string StudentSubjectIdHash { get; init; } = "";

    /// <summary>Admin subject id who confirmed the error and issued the credit.</summary>
    public string CreditedBy { get; init; } = "";

    /// <summary>Credit flavor. See <see cref="DiagnosticCreditKind"/>.</summary>
    public DiagnosticCreditKind CreditKind { get; init; }

    /// <summary>
    /// How many free uploads were granted. PhotoDiagnosticQuotaGate
    /// subtracts the sum of this field (across rows in the same month)
    /// from the raw monthly usage before checking caps.
    /// </summary>
    public int UploadQuotaBumpCount { get; init; }

    /// <summary>When the credit was issued.</summary>
    public DateTimeOffset IssuedAtUtc { get; init; }

    /// <summary>
    /// Free-text explanation recorded for audit. Matches what the apology
    /// email shows the student. Bounded at <see cref="DiagnosticCreditService.MaxReasonLength"/>.
    /// </summary>
    public string Reason { get; init; } = "";
}
