// =============================================================================
// Cena Platform — PerPaperSittingOverrideCleared_V1 (prr-243, ADR-0050 §1)
//
// Emitted when a per-שאלון sitting override is removed, so the שאלון once
// again inherits the target's primary Sitting. Idempotent — clearing a
// non-existent override is a no-op at the fold (the aggregate just
// observes a map-key miss).
//
// Stream key: `studentplan-{studentAnonId}`.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// A per-שאלון sitting override was cleared. Applied to
/// <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TargetId">The target carrying the override.</param>
/// <param name="PaperCode">Ministry numeric שאלון code whose override is
/// being cleared.</param>
/// <param name="ClearedAt">Wall-clock of the clear.</param>
public sealed record PerPaperSittingOverrideCleared_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode,
    DateTimeOffset ClearedAt);
