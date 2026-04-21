// =============================================================================
// Cena Platform — ExamTargetArchived_V1 (prr-218, ADR-0050 §6)
//
// Emitted when a target is archived — the terminal state. Archived targets
// are preserved for 24 months (user-extendable to 60 months per ADR-0050
// §6) so retrospective analytics can examine the student's declared-plan
// history without keeping indefinite open state. The retention worker
// (PRR-015) crypto-shreds the target + its events once the retention
// window elapses.
//
// Re-archiving an already-archived target is an invariant violation at
// the command handler (§6: ArchivedAt is terminal); this event shape
// does not express that — the constraint is upstream.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// An active target was archived. Archival is soft; the target remains
/// in the stream with ArchivedAt set. Terminal state.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target being archived.</param>
/// <param name="ArchivedAt">Wall-clock of the archive.</param>
/// <param name="Reason">Structured enum reason (kept intentionally small —
/// free-text rejected per ADR-0050 §1).</param>
public sealed record ExamTargetArchived_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    DateTimeOffset ArchivedAt,
    ArchiveReason Reason);

/// <summary>
/// Why a target was archived. Enum-only — no free-text per ADR-0050 §1.
/// </summary>
public enum ArchiveReason
{
    /// <summary>Student declared they no longer want this target.</summary>
    StudentDeclined = 0,

    /// <summary>Target was completed (exam taken, moving on).</summary>
    Completed = 1,

    /// <summary>Enrollment changed (teacher moved student; ADR-0001).</summary>
    EnrollmentChanged = 2,

    /// <summary>Catalog retired this exam (ministry deprecated).</summary>
    CatalogRetired = 3,

    /// <summary>Migration rollback (prr-219; target re-archived as part of
    /// migration reversal).</summary>
    MigrationRollback = 4,
}
