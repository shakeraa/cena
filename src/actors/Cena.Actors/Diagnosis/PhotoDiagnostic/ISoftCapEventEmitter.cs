// =============================================================================
// Cena Platform — ISoftCapEventEmitter (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// Narrow write-side port the PhotoDiagnosticQuotaGate (and future cap
// gates) call to emit <c>EntitlementSoftCapReached_V1</c> on the parent's
// subscription stream — exactly once per (student, cap-type, month)
// period. The once-per-period invariant is enforced by the ledger the
// emitter composes in (<see cref="ISoftCapEmissionLedger"/>). See
// <c>SoftCapEmissionLedgerDocument.cs</c> for why we use a ledger-doc
// instead of scanning the event stream on every hot-path call.
//
// Default DI binding is <c>NullSoftCapEventEmitter</c> — a legitimate
// no-subscription-store fallback (mirrors the NullEmailSender pattern,
// not a stub). Hosts that have AddSubscriptions(...) wired replace the
// binding with <c>SoftCapEventEmitter</c> during composition.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface ISoftCapEventEmitter
{
    /// <summary>
    /// Emit <c>EntitlementSoftCapReached_V1</c> onto the parent's
    /// subscription stream IFF this is the first call for the tuple
    /// (studentSubjectIdHash, capType, UTC month of <paramref name="nowUtc"/>).
    /// Subsequent calls for the same tuple in the same month are
    /// idempotent no-ops (the ledger rejects the claim).
    /// </summary>
    /// <param name="studentSubjectIdHash">Hashed student subject id (for ledger keying).</param>
    /// <param name="parentSubjectIdEncrypted">
    /// Encrypted parent subject id. Used both as the subscription stream
    /// key (via <c>SubscriptionAggregate.StreamKey</c>) and as the event
    /// payload field. Caller MUST have already resolved the binding.
    /// </param>
    /// <param name="capType">One of <see cref="Subscriptions.Events.EntitlementSoftCapReached_V1.CapTypes"/>.</param>
    /// <param name="usageCount">The current usage count at the moment the cap was crossed.</param>
    /// <param name="capLimit">The soft cap that was reached.</param>
    /// <param name="nowUtc">Wall-clock. Used as the event timestamp AND as the month-window source.</param>
    /// <param name="ct">Cancellation.</param>
    Task EmitIfFirstInPeriodAsync(
        string studentSubjectIdHash,
        string parentSubjectIdEncrypted,
        string capType,
        int usageCount,
        int capLimit,
        DateTimeOffset nowUtc,
        CancellationToken ct);
}
