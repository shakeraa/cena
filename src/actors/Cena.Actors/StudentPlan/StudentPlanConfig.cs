// =============================================================================
// Cena Platform — StudentPlanConfig value object (prr-148)
//
// Small read-model VO that folds the StudentPlanAggregate state into the
// exact shape AdaptiveScheduler.SchedulerInputs needs (deadline + weekly
// budget). Kept intentionally thin — this is *not* the aggregate state
// itself (see StudentPlanState).
//
// NOTE: distinct from Cena.Actors.Sessions.StudentPlanConfig (prr-149).
// The Sessions variant is the scheduler-facing bundle that adds
// MotivationProfile + applies defaults; this one is the raw write-side
// projection. prr-149 owns the bridge that combines them.
//
// Null DeadlineUtc / null WeeklyBudget => "student has not set this yet".
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Projected study-plan inputs as recorded on the write-side stream.
/// </summary>
/// <param name="StudentAnonId">Already-derived pseudonymous student id.</param>
/// <param name="DeadlineUtc">Target exam date, or null if unset.</param>
/// <param name="WeeklyBudget">Target weekly study time, or null if unset.</param>
/// <param name="UpdatedAt">Wall-clock of the last update, or null if unset.</param>
public sealed record StudentPlanConfig(
    string StudentAnonId,
    DateTimeOffset? DeadlineUtc,
    TimeSpan? WeeklyBudget,
    DateTimeOffset? UpdatedAt);
