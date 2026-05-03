// =============================================================================
// Cena Platform — NullDiagnosticCreditDispatcher (EPIC-PRR-J PRR-391)
//
// Default binding for IDiagnosticCreditDispatcher when no outbound email/
// SMS channel is configured for the credit-apology flow. Ships NOW so
// PRR-391's backend path is production-safe the moment the endpoint is
// exercised: the credit lands in the ledger, the quota gate honours it,
// and the dispatcher logs a structured NOT_CONFIGURED line so ops sees
// the gap without the admin request 500-ing.
//
// This is the same fallback pattern as NullSmsSender (PRR-428) and
// NullErrorAggregator. It is NOT a stub — it is a legitimate "feature
// not configured in this environment" implementation. The credit effect
// on the student account (the only thing PRR-391's DoD actually cares
// about) happens regardless; the notification is best-effort and will
// be upgraded by the content team's email-template task to an
// EmailDiagnosticCreditDispatcher binding.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class NullDiagnosticCreditDispatcher : IDiagnosticCreditDispatcher
{
    private readonly ILogger<NullDiagnosticCreditDispatcher> _logger;

    public NullDiagnosticCreditDispatcher(ILogger<NullDiagnosticCreditDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DiagnosticCreditDispatchResult> DispatchAsync(
        DiagnosticCreditDispatchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Structured log so SIEM/ops can alert on credits-issued-but-
        // not-notified. StudentSubjectIdHash is already hashed upstream
        // so logging it is consistent with the rest of PhotoDiagnostic.
        _logger.LogInformation(
            "[prr-391] NullDiagnosticCreditDispatcher NOT_CONFIGURED "
            + "creditId={CreditId} disputeId={DisputeId} student={StudentHash} "
            + "kind={Kind} uploadBump={UploadBump} — ledger row is written; "
            + "student will see the credit effect on their next diagnostic "
            + "check, but no apology notification was sent.",
            request.Ledger.Id,
            request.Ledger.DisputeId,
            request.StudentSubjectIdHash,
            request.Ledger.CreditKind,
            request.Ledger.UploadQuotaBumpCount);

        return Task.FromResult(new DiagnosticCreditDispatchResult(
            Delivered: false,
            Channel: "null",
            ErrorCode: "NOT_CONFIGURED"));
    }
}
