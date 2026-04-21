// =============================================================================
// Cena Platform — ExamTargetCompleted_V1 (prr-218)
//
// Emitted when a target is completed (exam taken, student moved on). This
// is distinct from "archived" because it carries semantic meaning ("the
// student actually reached the goal") that analytics downstream cares
// about — "completion rate per target" vs "archive rate per target".
//
// Lifecycle: Completed is a sibling of Archived — a target transitions
// from Active to either Completed or Archived, never both. Completing a
// target also sets ArchivedAt (terminal) with
// ArchiveReason=Completed so the aggregate invariant "archived ⇒ no
// further writes" holds uniformly.
//
// Note: this event records the *decision* to mark completed; the actual
// exam-result signal (score, pass/fail) is out of scope here and lives on
// the exam-attempt stream (future work).
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// An active target was marked completed. Equivalent to
/// <see cref="ExamTargetArchived_V1"/> with
/// <see cref="ArchiveReason.Completed"/>, but carries richer semantic
/// intent for downstream analytics.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target being completed.</param>
/// <param name="CompletedAt">Wall-clock of the completion.</param>
public sealed record ExamTargetCompleted_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    DateTimeOffset CompletedAt);
