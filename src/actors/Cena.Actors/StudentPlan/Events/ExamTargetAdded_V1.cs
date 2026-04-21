// =============================================================================
// Cena Platform — ExamTargetAdded_V1 (prr-218, ADR-0050 §1)
//
// Emitted when a student / teacher / tenant admin adds a new exam target to
// a student's plan. Stream key: `studentplan-{studentAnonId}`.
//
// Design:
//   - Pure data event; no invariants re-checked on fold. The command
//     handler (StudentPlanAggregate.AddExamTarget) enforces §5 invariants
//     BEFORE emitting this event — once it's on the stream, it happened.
//   - Crypto-shreddable per ADR-0038: the StudentAnonId ties the event to
//     the student's subject-key; on account deletion, the key is purged
//     and this event (and all target events) decrypt to empty. No per-
//     field encryption is needed beyond the id binding.
//   - ReasonTag is nullable — students are not required to supply a
//     motive. UI hides the field in "quick add" flow.
//   - The event carries the full immutable ExamTarget record, not just the
//     delta, so replay is deterministic without looking up the initial
//     state.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// A new exam target has been added to a student's plan. Appended to
/// <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id (subject-key
/// binding per ADR-0038).</param>
/// <param name="Target">The full target record. Immutable; on replay the
/// aggregate rehydrates the target list directly from these events.</param>
/// <param name="SetAt">Wall-clock of the add.</param>
public sealed record ExamTargetAdded_V1(
    string StudentAnonId,
    ExamTarget Target,
    DateTimeOffset SetAt);
