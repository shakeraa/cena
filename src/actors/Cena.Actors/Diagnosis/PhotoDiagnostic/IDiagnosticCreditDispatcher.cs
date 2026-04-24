// =============================================================================
// Cena Platform — IDiagnosticCreditDispatcher (EPIC-PRR-J PRR-391)
//
// Seam for side-effects triggered by a confirmed-dispute credit issuance —
// primarily the auto-apology email in the student's locale. Kept separate
// from the service orchestration so that:
//   - the DiagnosticCreditService stays a pure domain orchestration
//     (dispute status flip + ledger write) and stays easy to unit-test;
//   - the host can bind whatever downstream channel is available (email
//     first, later SMS/in-app inbox) without touching the domain;
//   - the null-fallback binding is a legitimate "not-configured" mode,
//     not a stub: the credit still lands in the ledger and the quota
//     gate still honours it — only the outbound notification drops.
//
// The default binding is NullDiagnosticCreditDispatcher, which logs the
// intended send and returns a structured NOT_CONFIGURED result. Once the
// email template/content-team work lands, the admin host will swap to
// an EmailDiagnosticCreditDispatcher that wraps IEmailSender. This is
// the same pattern as NullSmsSender / NullWhatsAppSender (PRR-428).
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Input to <see cref="IDiagnosticCreditDispatcher.DispatchAsync"/>. Carries
/// only what the dispatcher needs — no raw diagnostic, no PII. The student
/// hash lets the dispatcher resolve an email address via a separate
/// identity lookup (not coupled here) in the production binding.
/// </summary>
/// <param name="Ledger">The credit row that was just written.</param>
/// <param name="StudentSubjectIdHash">Same value as Ledger.StudentSubjectIdHash, denormalised for clarity.</param>
public sealed record DiagnosticCreditDispatchRequest(
    DiagnosticCreditLedgerDocument Ledger,
    string StudentSubjectIdHash);

/// <summary>Structured outcome returned by a dispatch attempt.</summary>
/// <param name="Delivered">True iff the downstream channel accepted the message.</param>
/// <param name="Channel">Free-text channel identifier (email, sms, null, etc.).</param>
/// <param name="ErrorCode">Stable code when Delivered is false; e.g. "NOT_CONFIGURED".</param>
public sealed record DiagnosticCreditDispatchResult(
    bool Delivered,
    string Channel,
    string? ErrorCode);

/// <summary>Port for sending the apology notification when a credit is issued.</summary>
public interface IDiagnosticCreditDispatcher
{
    Task<DiagnosticCreditDispatchResult> DispatchAsync(
        DiagnosticCreditDispatchRequest request, CancellationToken ct);
}
