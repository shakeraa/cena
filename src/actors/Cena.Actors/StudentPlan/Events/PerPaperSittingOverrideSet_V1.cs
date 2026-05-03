// =============================================================================
// Cena Platform — PerPaperSittingOverrideSet_V1 (prr-243, ADR-0050 §1)
//
// Emitted when a per-שאלון sitting override is set (create-or-update) on
// an active Bagrut target. This event both creates a new entry in the
// target's PerPaperSittingOverride map AND replaces an existing entry —
// the fold is overwrite-on-key to avoid a distinct "updated" event.
//
// Invariants (enforced at StudentPlanCommandHandler BEFORE emit):
//   - Target exists, is active.
//   - PaperCode ∈ target.QuestionPaperCodes (ADR-0050 §1 "keys ⊆ papers").
//   - Override sitting ≠ target.Sitting (minimal-map invariant, §1).
//
// Stream key: `studentplan-{studentAnonId}`.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// A per-שאלון sitting override was set on an active Bagrut target.
/// Applied to <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target carrying the override.</param>
/// <param name="PaperCode">Ministry numeric שאלון code being overridden
/// — must already be in <see cref="ExamTarget.QuestionPaperCodes"/>.</param>
/// <param name="Sitting">New sitting for this שאלון; must differ from the
/// target's primary <see cref="ExamTarget.Sitting"/>.</param>
/// <param name="SetAt">Wall-clock of the override set.</param>
public sealed record PerPaperSittingOverrideSet_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode,
    SittingCode Sitting,
    DateTimeOffset SetAt);
