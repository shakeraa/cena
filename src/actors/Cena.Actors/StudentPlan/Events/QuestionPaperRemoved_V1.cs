// =============================================================================
// Cena Platform — QuestionPaperRemoved_V1 (prr-243, ADR-0050 §1)
//
// Emitted when a student (or their teacher) drops a שאלון from an active
// Bagrut target. Aggregate invariant (ADR-0050 §1, PRR-243 DoD): removal
// is rejected if it would leave a Bagrut-family target with zero paper
// codes — students must archive the target instead.
//
// Any per-paper sitting override keyed by the removed code is cleared as
// a side effect during the fold; callers do not need to emit a separate
// PerPaperSittingOverrideCleared_V1 for the same code.
//
// Stream key: `studentplan-{studentAnonId}`.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// A Ministry שאלון code was removed from an existing active Bagrut
/// target. Applied to <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target being shrunk.</param>
/// <param name="PaperCode">Ministry numeric שאלון code being removed.</param>
/// <param name="RemovedAt">Wall-clock of the removal.</param>
public sealed record QuestionPaperRemoved_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode,
    DateTimeOffset RemovedAt);
