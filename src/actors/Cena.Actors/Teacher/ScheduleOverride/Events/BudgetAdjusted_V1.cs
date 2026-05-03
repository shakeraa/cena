// =============================================================================
// Cena Platform — TeacherOverrideAggregate event: BudgetAdjusted_V1 (prr-150)
//
// Emitted when a teacher / tutor / mentor overrides the student's weekly
// time budget (for example: an exam-prep push where the teacher temporarily
// raises the budget from 5h to 12h). Appended to
// `teacheroverride-{studentAnonId}`.
//
// Precedence — this event overrides whatever the student set on their own
// StudentPlan stream (prr-148). When a BudgetAdjusted_V1 is active, the
// scheduler reads WeeklyTimeBudget from THIS stream, not from prr-148.
//
// Validation lives in the command handler (1h..40h, matching prr-148's
// bounds). The event record itself is pure data so historical replays do
// not re-validate against future policy changes.
// =============================================================================

namespace Cena.Actors.Teacher.ScheduleOverride.Events;

/// <summary>
/// Teacher adjusted the student's weekly study-time budget. Appended to
/// <c>teacheroverride-{studentAnonId}</c>. Latest event wins on replay.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="NewWeeklyBudget">Override budget. Command handler enforces bounds.</param>
/// <param name="TeacherActorId">Pseudonymous teacher id for audit trail.</param>
/// <param name="InstituteId">Tenant id — verified by the command handler.</param>
/// <param name="Rationale">Free-text teacher audit rationale.</param>
/// <param name="SetAt">Wall-clock of the teacher's action (UTC).</param>
public sealed record BudgetAdjusted_V1(
    string StudentAnonId,
    TimeSpan NewWeeklyBudget,
    string TeacherActorId,
    string InstituteId,
    string Rationale,
    DateTimeOffset SetAt);
