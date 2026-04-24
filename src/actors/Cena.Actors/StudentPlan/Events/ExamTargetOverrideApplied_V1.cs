// =============================================================================
// Cena Platform — ExamTargetOverrideApplied_V1 (prr-218)
//
// Emitted when the scheduler is asked to run against a specific target for
// a session, overriding the normal "pick active target by priority"
// heuristic. Pure telemetry — no behaviour change at the aggregate level;
// downstream observability consumers aggregate override-rate per student
// to detect scheduler-adoption regressions.
//
// This event is a NO-OP on aggregate state. The fold ignores it for
// invariants; it exists on the stream so audit / analytics projections can
// count overrides without scraping session logs. Per prr-218 scope "no
// behavior change".
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// Scheduler telemetry event — the student (or the scheduler heuristic)
/// picked a different target for a specific session.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target the scheduler ran against.</param>
/// <param name="SessionId">The session the override applied to.</param>
/// <param name="AppliedAt">Wall-clock of the override.</param>
public sealed record ExamTargetOverrideApplied_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    string SessionId,
    DateTimeOffset AppliedAt);
