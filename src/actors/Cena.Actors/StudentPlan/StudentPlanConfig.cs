// =============================================================================
// Cena Platform — StudentPlanConfig value object (prr-148, SUPERSEDED by prr-218)
//
// Legacy-projection shape. Retained solely as the wire contract between
// the multi-target StudentPlan aggregate (prr-218) and the Sessions
// scheduler bridge (prr-149), which still consumes a single {deadline,
// weeklyBudget} pair. Not authored by students directly — the only
// writer is <see cref="StudentPlanAggregate.ToConfig"/>, which projects
// the multi-target state down to this shape.
//
// Follow-up per prr-234: replace this VO with a direct multi-target
// consumption path inside Cena.Actors.Sessions (bridge reads the active
// target list + catalog-resolved canonical date) once PRR-220 exam
// catalog lands. At that point this file can be deleted. Today's
// migration window keeps it for backward compatibility so the scheduler
// does not require a lock-step catalog rollout.
//
// NOTE: distinct from Cena.Actors.Sessions.StudentPlanConfig (prr-149).
// The Sessions variant is the scheduler-facing bundle that adds
// MotivationProfile + applies defaults; this one is the raw write-side
// projection.
//
// Null DeadlineUtc / null WeeklyBudget => "student has no active target
// AND no legacy scalar events on the stream".
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// DEPRECATED (prr-234): legacy projection shape retained as an internal
/// bridge between <see cref="StudentPlanAggregate"/> and the Sessions
/// scheduler bridge. Do not introduce new callers; use
/// <see cref="IStudentPlanReader"/> for multi-target reads instead.
/// </summary>
/// <param name="StudentAnonId">Already-derived pseudonymous student id.</param>
/// <param name="DeadlineUtc">Projected deadline. Null when no active
/// target and no legacy event is on the stream. Multi-target callers
/// MUST use <see cref="ExamTarget.Sitting"/> + catalog canonical dates
/// instead.</param>
/// <param name="WeeklyBudget">Sum of active targets' WeeklyHours, or
/// the legacy single-target budget when no active target. Null when
/// neither signal is available.</param>
/// <param name="UpdatedAt">Wall-clock of the last update, or null if
/// unset.</param>
public sealed record StudentPlanConfig(
    string StudentAnonId,
    DateTimeOffset? DeadlineUtc,
    TimeSpan? WeeklyBudget,
    DateTimeOffset? UpdatedAt);
