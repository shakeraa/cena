// =============================================================================
// Cena Platform — ExamTargetUpdated_V1 (prr-218, ADR-0050 §1)
//
// Emitted when an existing active exam target is modified. Per the task
// spec (prr-218), only the mutable fields are carried — immutable identity
// fields (Source, ExamCode, AssignedById) cannot change. Archived targets
// reject updates (aggregate invariant §6).
//
// Mutable on update:
//   - Track (student may have registered for the wrong track)
//   - Sitting (moved the sitting e.g. from Summer/A to Winter/A)
//   - WeeklyHours (rebalancing; sum across active targets still ≤ 40)
//   - ReasonTag (set / unset)
//
// Immutable (would fork identity): Id, Source, AssignedById, EnrollmentId,
// ExamCode, CreatedAt, ArchivedAt.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// An existing active target was updated. Stream:
/// <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target being updated.</param>
/// <param name="Track">New track (or null to clear).</param>
/// <param name="Sitting">New sitting tuple.</param>
/// <param name="WeeklyHours">New weekly-hours allocation (1..40).</param>
/// <param name="ReasonTag">New reason tag (or null to clear).</param>
/// <param name="SetAt">Wall-clock of the update.</param>
public sealed record ExamTargetUpdated_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    TrackCode? Track,
    SittingCode Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag,
    DateTimeOffset SetAt);
