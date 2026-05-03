// =============================================================================
// Cena Platform — QuestionPaperAdded_V1 (prr-243, ADR-0050 §1)
//
// Emitted when a student (or their teacher) appends a new שאלון (Ministry
// question-paper code) to an existing active Bagrut target post-hoc —
// e.g. the student originally picked שאלון 1 + 2 and later decides to
// sit for שאלון 3 as well. Standardized-family targets (SAT/PET) reject
// this command at the handler, so this event only appears on Bagrut-
// family streams.
//
// The initial set of שאלונים on target creation travels on
// ExamTargetAdded_V1.Target.QuestionPaperCodes; this event is the
// idempotent delta-add for the post-hoc path.
//
// Stream key: `studentplan-{studentAnonId}` (same stream as the target
// itself so replay observes a consistent target history).
//
// Invariants (enforced at StudentPlanCommandHandler BEFORE emit):
//   - Target exists, is active, is Bagrut-family.
//   - PaperCode is in the catalog for (ExamCode, Track).
//   - PaperCode is not already present (idempotent no-op swallowed up-stream).
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// A Ministry שאלון code was added post-hoc to an existing active Bagrut
/// target. Applied to <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target being extended.</param>
/// <param name="PaperCode">Ministry numeric שאלון code (e.g. "035583").
/// Always LTR; UI wraps it in <c>&lt;bdi dir="ltr"&gt;</c> for RTL locales
/// per the "Math always LTR" memory.</param>
/// <param name="SittingOverride">Optional sitting override for the newly-
/// added paper. Null = use the target's primary <see cref="ExamTarget.Sitting"/>.
/// Non-null = the student is splitting this שאלון across a different
/// sitting (e.g. Grade-11 Summer for שאלון 1, Grade-12 Summer for the
/// rest). Value must differ from the target's primary sitting (minimal-
/// map invariant, ADR-0050 §1).</param>
/// <param name="AddedAt">Wall-clock of the add.</param>
public sealed record QuestionPaperAdded_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode,
    SittingCode? SittingOverride,
    DateTimeOffset AddedAt);
