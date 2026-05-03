// =============================================================================
// Cena Platform — LearningSession event: SessionPlanComputed_V1 (prr-149)
//
// Appended to the `session-{SessionId}` stream immediately after the
// scheduler has produced its initial plan for a newly-started session.
// Session-scoped — NEVER appended to the student stream. ADR-0012 schedule
// lock: new session-owned events land in the session stream by default.
//
// Payload is the plan's priority-ordered topic list plus a small amount
// of metadata the downstream projection / API consumers need without
// round-tripping through the scheduler. The motivation profile is
// carried verbatim so the rendering layer never guesses.
//
// Read path: GET /api/session/{sessionId}/plan (prr-149). No StudentActor
// projection consumes this event — it is session-local by design.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Sessions.Events;

/// <summary>
/// Event marking that an AdaptiveScheduler plan has been computed for
/// the session identified by <paramref name="SessionId"/>.
/// <para>
/// Stream: <c>session-{SessionId}</c>. Never appended to the student
/// stream (that would turn a session-local recommendation into a
/// profile attribute, which prr-013 and ADR-0003 both forbid for the
/// adjacent risk/readiness surface).
/// </para>
/// </summary>
/// <param name="StudentAnonId">Session owner, anon id form.</param>
/// <param name="SessionId">Session stream id.</param>
/// <param name="GeneratedAtUtc">Wall-clock time of the scheduler run.</param>
/// <param name="Topics">Priority-ordered topics with their rationale.
/// Matches AdaptiveScheduler's output order.</param>
/// <param name="MotivationProfile">Student's motivation stance at plan
/// time; drives the rationale-copy template on the client.</param>
/// <param name="DeadlineUtc">Deadline the scheduler planned against, if
/// the student had one configured.</param>
/// <param name="WeeklyBudgetMinutes">Weekly minute budget used as
/// scheduler input.</param>
/// <param name="InputsSource">How the scheduler inputs were populated:
/// "student-plan-config" when prr-148's StudentPlanConfig service
/// supplied them; "default-fallback" when the in-memory default kicked
/// in because no config existed yet. Useful for observability — a
/// long-tail of "default-fallback" signals prr-148 adoption has
/// stalled.</param>
public sealed record SessionPlanComputed_V1(
    string StudentAnonId,
    string SessionId,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SessionPlanTopicEntry_V1> Topics,
    MotivationProfile MotivationProfile,
    DateTimeOffset? DeadlineUtc,
    int WeeklyBudgetMinutes,
    string InputsSource);

/// <summary>
/// Flat, serialisation-stable form of <see cref="PlanEntry"/> used on
/// the event stream. Mirrors the scheduler output. The rationale string
/// is the motivation-safe copy the scheduler built; we store it on the
/// event so the read path does not have to re-run the scheduler just
/// to render "why is this on my plan?".
/// </summary>
public sealed record SessionPlanTopicEntry_V1(
    string TopicSlug,
    double PriorityScore,
    double WeaknessComponent,
    double TopicWeightComponent,
    double PrerequisiteComponent,
    string Rationale);
