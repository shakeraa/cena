// =============================================================================
// Cena Platform — StudentPlanAggregate event: ExamDateSet_V1 (prr-148)
//
// Emitted when a student sets or updates their target exam date (typically
// the Bagrut sitting date). Appended to the stream `studentplan-{studentId}`.
//
// Design notes:
//   - StudentAnonId is deliberately an already-derived anonymous id, not a
//     Firebase uid. The Student API host derives it via the standard
//     student_id JWT claim before calling the command surface, so this
//     event type does not itself need ADR-0038 crypto-shredding (the id is
//     pseudonymous by construction). If that assumption ever changes,
//     bump to V2 and encrypt via EncryptedFieldAccessor.
//   - DeadlineUtc is the authoritative wall-clock the AdaptiveScheduler
//     consumes via SchedulerInputs.DeadlineUtc. It is always UTC; the
//     frontend converts from the student's local tz before PUT.
//   - SetAt is the audit timestamp for when the student made the change.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// Student set or updated their target exam date. Appended to
/// <c>studentplan-{studentId}</c>. Latest event wins on replay.
/// </summary>
/// <param name="StudentAnonId">Already-derived pseudonymous student id.</param>
/// <param name="DeadlineUtc">Target exam date (always UTC).</param>
/// <param name="SetAt">Wall-clock of the student's action.</param>
public sealed record ExamDateSet_V1(
    string StudentAnonId,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset SetAt);
