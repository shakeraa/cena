// =============================================================================
// Cena Platform — TeacherOverrideAggregate event: PinTopicRequested_V1 (prr-150)
//
// Emitted when a teacher / tutor / mentor asks the scheduler to keep a
// specific topic at the top of the student's next N sessions. Appended to
// the stream `teacheroverride-{studentAnonId}`.
//
// Design notes:
//   - StudentAnonId and TeacherActorId are pseudonymous ids — already
//     derived before reaching the command handler (same convention as
//     prr-148's StudentPlan events). The handler has already verified the
//     teacher's institute matches the student's active enrollment, so
//     consumers can trust the InstituteId field here without re-checking.
//   - PinnedSessionCount is a decrementing counter; it is NOT a wall-clock
//     deadline. The aggregate fold decrements on each consumed session.
//   - Rationale is free-text audit copy (e.g. "student failing log-scale
//     questions, need 3 more drill sessions"). Not shown to the student.
//   - This event is LATEST-ACTIVE-WINS on the same TopicSlug: a second
//     PinTopicRequested_V1 for the same slug replaces the prior pin's
//     countdown; different slugs coexist.
// =============================================================================

namespace Cena.Actors.Teacher.ScheduleOverride.Events;

/// <summary>
/// Teacher pinned a topic for the student's next N sessions. Appended to
/// <c>teacheroverride-{studentAnonId}</c>. Active until the counter decays
/// to zero or a <c>PinRemoved_V1</c> clears it (future event).
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id (the override target).</param>
/// <param name="TopicSlug">Topic to keep prioritised in upcoming sessions.</param>
/// <param name="PinnedSessionCount">How many sessions the pin stays active for. Must be &gt;= 1; the aggregate enforces the bound in the command layer.</param>
/// <param name="TeacherActorId">Pseudonymous teacher id for audit trail.</param>
/// <param name="InstituteId">Tenant id — handler has already verified this matches the student's active enrollment institute.</param>
/// <param name="Rationale">Free-text teacher audit rationale. Never shown to the student.</param>
/// <param name="SetAt">Wall-clock of the teacher's action (UTC).</param>
public sealed record PinTopicRequested_V1(
    string StudentAnonId,
    string TopicSlug,
    int PinnedSessionCount,
    string TeacherActorId,
    string InstituteId,
    string Rationale,
    DateTimeOffset SetAt);
