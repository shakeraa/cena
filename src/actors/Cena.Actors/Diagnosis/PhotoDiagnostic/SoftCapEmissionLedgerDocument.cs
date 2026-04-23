// =============================================================================
// Cena Platform — SoftCapEmissionLedgerDocument (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// EntitlementSoftCapReached_V1 is a telemetry event — it fans out to
// AbuseDetectionWorker (30-day abuse flag) and to the ParentDashboard
// projection, and it must fire EXACTLY ONCE per (student, cap-type,
// month-window). The photo-diagnostic intake path hits the quota gate on
// every upload; when Decision == SoftCapReached the gate is called
// repeatedly after the student passes 100 uploads (uploads 101, 102, ...
// all return SoftCapReached). We must emit the telemetry event on the
// first of those checks and be a cheap no-op on the rest — otherwise:
//   - AbuseDetectionWorker over-counts by the number of post-cap uploads,
//     causing false 200-upload abuse flags on normal power users
//   - the ParentDashboard soft-cap-reached card re-renders on every upload
//   - the upsell telemetry stream becomes noise instead of signal
//
// Ledger-not-event-stream-scan rationale
// --------------------------------------
// The dedup invariant could in principle be enforced by scanning the
// parent's subscription event stream on every emit and looking for an
// existing EntitlementSoftCapReached_V1 in the same (student, cap, month).
// That read is O(events-in-stream) and costs a full stream load on every
// upload past the soft cap. A tiny ledger document keyed by the compound
// tuple is O(1), survives restarts, and matches the pattern already used
// by MartenProcessedWebhookLog (Stripe webhook dedup) and
// ExamTargetShredLedgerDocument (retention sweep dedup). Concretely:
//   - presence of a row == "already emitted this period" (read: return)
//   - absence == "first emission; claim the row + append the event"
//   - optimistic concurrency on the doc id handles the two-racer case
//
// Compound id shape: "{studentSubjectIdHash}|{capType}|{YYYY-MM}". Each
// component is either already-hashed (student id) or a fixed well-known
// string (cap type / month key), so the compound id is safe to persist
// unencrypted. A second upload on the 101st minute of the same calendar
// month produces the same id → Marten's DocumentAlreadyExistsException
// path tells the emitter "already emitted; bail".
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Presence-only dedup row for the once-per-(student, cap-type, month)
/// emission invariant of <c>EntitlementSoftCapReached_V1</c>. One row per
/// unique tuple, ever. The row itself never mutates; a second emission
/// attempt for the same tuple is prevented by the uniqueness of
/// <see cref="Id"/>.
/// </summary>
public sealed record SoftCapEmissionLedgerDocument
{
    /// <summary>
    /// Compound primary key: <c>{studentSubjectIdHash}|{capType}|{monthWindow}</c>
    /// where <c>monthWindow</c> is the canonical <c>YYYY-MM</c> emitted
    /// by <see cref="MonthlyUsageKey.For"/>. Built via
    /// <see cref="KeyOf(string,string,string)"/> to keep separator
    /// discipline in one place.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>Hashed student subject id (matches other PhotoDiagnostic docs).</summary>
    public string StudentSubjectIdHash { get; init; } = "";

    /// <summary>
    /// Cap-type token. One of <see cref="Subscriptions.Events.EntitlementSoftCapReached_V1.CapTypes"/>.
    /// Stored verbatim so future cap types drop in without a schema migration.
    /// </summary>
    public string CapType { get; init; } = "";

    /// <summary>Canonical <c>YYYY-MM</c> month key (see <see cref="MonthlyUsageKey.For"/>).</summary>
    public string MonthWindow { get; init; } = "";

    /// <summary>Wall-clock of the first emission, for audit + forensics.</summary>
    public DateTimeOffset EmittedAtUtc { get; init; }

    /// <summary>
    /// Construct the compound id for a given tuple. Separator is <c>|</c>
    /// which is forbidden in all three components by upstream validation
    /// (hashed ids are hex, cap-type tokens are snake-lowercase, month
    /// keys are <c>YYYY-MM</c>).
    /// </summary>
    public static string KeyOf(string studentSubjectIdHash, string capType, string monthWindow)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(capType))
            throw new ArgumentException("capType is required.", nameof(capType));
        if (string.IsNullOrWhiteSpace(monthWindow))
            throw new ArgumentException("monthWindow is required.", nameof(monthWindow));
        return string.Concat(studentSubjectIdHash, "|", capType, "|", monthWindow);
    }
}
