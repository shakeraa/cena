// =============================================================================
// Cena Platform — ExamTargetShredLedgerDocument (prr-229 prod binding)
//
// Marten-persisted shred marker. One row per (studentAnonId, examTargetCode)
// that the ExamTargetRetentionWorker has already shredded. Lets the
// MartenArchivedExamTargetSource filter out already-shredded targets so
// sweeps monotonically progress instead of looping on the same row every
// run.
//
// Why a ledger instead of an event: shred is an operational action (the
// worker deleted mastery rows + notified the student) — it's not part of
// the student's plan history. Emitting an event on every shred would
// pollute the StudentPlan stream with ops telemetry. The ledger is a
// document-style Marten store with a compound string id.
// =============================================================================

namespace Cena.Actors.Retention;

/// <summary>
/// Marker row written by the retention worker after a successful shred so
/// the next sweep does not re-enumerate the same (student, target) pair.
/// </summary>
public sealed record ExamTargetShredLedgerDocument
{
    /// <summary>
    /// Compound id <c>{studentAnonId}|{examTargetCode.Value}</c>. The
    /// retention worker writes this on MarkShreddedAsync; the Marten
    /// source filters enumerated archived targets through this set.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>Wall-clock of the shred, for audit + forensics.</summary>
    public DateTimeOffset ShreddedAtUtc { get; init; }
}
