// =============================================================================
// Cena Platform — SoftCapEventEmitter (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// Production implementation of <see cref="ISoftCapEventEmitter"/>. Composes:
//   - <see cref="ISoftCapEmissionLedger"/> — enforces once-per-(student,
//     cap, month) invariant via an O(1) compound-id dedup row
//   - <see cref="ISubscriptionAggregateStore"/> — appends the event to
//     the parent's subscription stream (stream key: "subscription-{parentId}")
//
// Flow on a quota-gate CheckAsync that returned SoftCapReached:
//   1. QuotaGate already computed Decision=SoftCapReached and knows the
//      student hash + cap type + usage + cap. It calls EmitIfFirst...
//      with those values plus the parent's encrypted subject id that it
//      resolved through IStudentEntitlementResolver.
//   2. Ledger.TryClaim compares the compound id; on the first call for
//      the tuple it claims + returns true, on all later calls within
//      the same month it returns false.
//   3. On true, we build the EntitlementSoftCapReached_V1 event with
//      ReachedAt=nowUtc and AppendAsync to the parent's stream via the
//      subscription aggregate store. The event fans out to:
//        - AbuseDetectionWorker (30-day window)
//        - ParentDashboard soft-cap-reached card (PRR-386 UX)
//        - StudentEntitlementProjection (read-model)
//   4. On false, we return without appending — by contract the event
//      was already emitted this period.
//
// Error handling
// --------------
// A ledger claim that succeeds MUST NOT be silently followed by an
// aggregate-store append that fails — that would leave the ledger
// marked "emitted" while the event never landed, and all subsequent
// calls this month would no-op. We catch any exception from
// AppendAsync, log it with context, and re-throw. The quota gate's
// caller handles the exception (see PhotoDiagnosticQuotaGate for the
// fire-and-forget-but-awaited wiring). Re-throwing is the right
// default — a silent swallow would hide a genuine storage outage, and
// the caller's retry policy (endpoint re-try or user's next upload)
// will eventually re-emit; the ledger row we just wrote is effectively
// a "we tried to emit" marker and not a correctness violation if the
// append eventually lands in a later retry. (Compensating delete of
// the ledger row is a future refinement tracked in PRR-401-FUP.)
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class SoftCapEventEmitter : ISoftCapEventEmitter
{
    private readonly ISoftCapEmissionLedger _ledger;
    private readonly ISubscriptionAggregateStore _subscriptionStore;
    private readonly ILogger<SoftCapEventEmitter> _logger;

    public SoftCapEventEmitter(
        ISoftCapEmissionLedger ledger,
        ISubscriptionAggregateStore subscriptionStore,
        ILogger<SoftCapEventEmitter> logger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EmitIfFirstInPeriodAsync(
        string studentSubjectIdHash,
        string parentSubjectIdEncrypted,
        string capType,
        int usageCount,
        int capLimit,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
            throw new ArgumentException("parentSubjectIdEncrypted is required.", nameof(parentSubjectIdEncrypted));
        if (string.IsNullOrWhiteSpace(capType))
            throw new ArgumentException("capType is required.", nameof(capType));

        var monthWindow = MonthlyUsageKey.For(nowUtc);
        var claimed = await _ledger
            .TryClaimAsync(studentSubjectIdHash, capType, monthWindow, nowUtc, ct)
            .ConfigureAwait(false);
        if (!claimed)
        {
            // Already emitted this period — idempotent no-op by contract.
            return;
        }

        var evt = new EntitlementSoftCapReached_V1(
            ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
            StudentSubjectIdEncrypted: studentSubjectIdHash,
            CapType: capType,
            UsageCount: usageCount,
            CapLimit: capLimit,
            ReachedAt: nowUtc);

        try
        {
            await _subscriptionStore
                .AppendAsync(parentSubjectIdEncrypted, evt, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[PRR-401] Failed to append EntitlementSoftCapReached_V1 capType={CapType} "
                + "usage={Usage} cap={Cap} after claiming ledger row. Ledger row remains; "
                + "compensating cleanup is a future refinement. Re-throwing.",
                capType, usageCount, capLimit);
            throw;
        }

        _logger.LogInformation(
            "[PRR-401] Emitted EntitlementSoftCapReached_V1 capType={CapType} usage={Usage} "
            + "cap={Cap} month={Month}",
            capType, usageCount, capLimit, monthWindow);
    }
}
