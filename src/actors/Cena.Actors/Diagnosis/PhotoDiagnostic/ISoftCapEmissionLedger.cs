// =============================================================================
// Cena Platform — ISoftCapEmissionLedger (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// Port for the tiny dedup store that backs
// <see cref="SoftCapEventEmitter.EmitIfFirstInPeriodAsync"/>. Two backends
// mirror the pattern already established by IDiagnosticCreditLedger /
// IHardCapSupportTicketRepository:
//   - InMemorySoftCapEmissionLedger — dev/test (thread-safe, concurrent)
//   - MartenSoftCapEmissionLedger    — production (survives restarts)
//
// Contract is intentionally two-method + presence-based:
//   - TryClaimAsync is idempotent: returns true only on first successful
//     claim for a given (student, cap, month). Any subsequent caller for
//     the same tuple sees false. Concurrent racers both calling TryClaim
//     at the same moment are serialized — at most one observes true.
//   - HasEmittedAsync is a read-only probe the emitter uses (and a future
//     ParentDashboard refresh path can use) to check state without
//     attempting a claim.
//
// No update / delete / bulk-query methods. The monthly reset is implicit:
// a new (student, cap, "2026-05") is simply a new row — the April row is
// left behind as audit history, never GC-ed (row volume is bounded by
// active-students × cap-types × months, < tens of thousands per year).
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface ISoftCapEmissionLedger
{
    /// <summary>
    /// Atomically record the first soft-cap emission for a given
    /// (student, cap-type, month) tuple.
    /// </summary>
    /// <param name="studentSubjectIdHash">Hashed student subject id.</param>
    /// <param name="capType">One of <see cref="Subscriptions.Events.EntitlementSoftCapReached_V1.CapTypes"/>.</param>
    /// <param name="monthWindow">Canonical <c>YYYY-MM</c> key (<see cref="MonthlyUsageKey.For"/>).</param>
    /// <param name="nowUtc">Wall-clock of the claim attempt (stored on the row).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// True if this call was the first to claim the tuple; false if a
    /// prior call (or a concurrent racer) already claimed it. Callers
    /// MUST treat the call as idempotent — false is not an error, it is
    /// the signal that the telemetry event was already emitted.
    /// </returns>
    Task<bool> TryClaimAsync(
        string studentSubjectIdHash,
        string capType,
        string monthWindow,
        DateTimeOffset nowUtc,
        CancellationToken ct);

    /// <summary>
    /// Read-only check: returns true if a row already exists for the
    /// tuple. Does NOT insert. Useful for read-side consumers that want
    /// to know "has telemetry been emitted this month?" without taking
    /// the claim-write path.
    /// </summary>
    Task<bool> HasEmittedAsync(
        string studentSubjectIdHash,
        string capType,
        string monthWindow,
        CancellationToken ct);
}
