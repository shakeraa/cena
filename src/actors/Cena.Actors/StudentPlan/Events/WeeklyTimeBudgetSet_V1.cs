// =============================================================================
// Cena Platform — StudentPlanAggregate event: WeeklyTimeBudgetSet_V1 (prr-148)
//
// Emitted when a student sets or updates their weekly time commitment for
// Cena study. Appended to the stream `studentplan-{studentId}`. Consumed by
// AdaptiveScheduler.SchedulerInputs.WeeklyTimeBudget at session-start.
//
// Validation boundary: the command surface enforces 1h ≤ WeeklyBudget ≤ 40h.
// The event itself does not re-validate (pure data) — the aggregate fold
// accepts any TimeSpan the command handler let through.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// Student set or updated their weekly study time budget. Appended to
/// <c>studentplan-{studentId}</c>. Latest event wins on replay.
/// </summary>
/// <param name="StudentAnonId">Already-derived pseudonymous student id.</param>
/// <param name="WeeklyBudget">Target weekly Cena study time.</param>
/// <param name="SetAt">Wall-clock of the student's action.</param>
public sealed record WeeklyTimeBudgetSet_V1(
    string StudentAnonId,
    TimeSpan WeeklyBudget,
    DateTimeOffset SetAt);
