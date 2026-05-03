// =============================================================================
// Cena Platform — ParentVisibilityChanged_V1 (prr-230)
//
// Emitted when a student (self-service) or system (birthday job, safety-flag
// carve-out) changes an exam target's parent-dashboard visibility. Stream
// key: `studentplan-{studentAnonId}`.
//
// Design:
//   - Pure data event; no invariants re-checked on fold. The command handler
//     validates age-band authority + target existence BEFORE emitting.
//   - Audit trail lives in event metadata (Initiator, Reason); the aggregate
//     state folds to the latest value per target.
//   - Back-compat: older ExamTargetAdded_V1 events have no ParentVisibility;
//     the fold defaults them to Visible, preserving prior always-visible
//     semantics.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// Who initiated the visibility change. Used by the audit log + architecture
/// tests that assert only allowed initiators touch each state.
/// </summary>
public enum ParentVisibilityChangeInitiator
{
    /// <summary>Student opted in/out via /api/me/exam-targets/{id}/visibility.</summary>
    Student = 0,

    /// <summary>System job (e.g. 18th-birthday auto-revoke, safety-flag carve-out).</summary>
    System = 1,
}

/// <summary>
/// A student target's parent-dashboard visibility has changed. Appended to
/// <c>studentplan-{studentAnonId}</c>.
/// </summary>
/// <param name="StudentAnonId">Plan owner (subject-key binding per
/// ADR-0038).</param>
/// <param name="TargetId">Which target changed.</param>
/// <param name="Visibility">New visibility state (Visible | Hidden).</param>
/// <param name="Initiator">Who initiated the change.</param>
/// <param name="InitiatorActorId">Actor id of the initiator — student id
/// for Student, system job id for System.</param>
/// <param name="Reason">Free-text audit reason (never rendered to UI);
/// examples: "student-self-opt-in", "safety-flag-carve-out",
/// "age-band-default".</param>
/// <param name="ChangedAt">Wall-clock of the change.</param>
public sealed record ParentVisibilityChanged_V1(
    string StudentAnonId,
    ExamTargetId TargetId,
    ParentVisibility Visibility,
    ParentVisibilityChangeInitiator Initiator,
    string InitiatorActorId,
    string Reason,
    DateTimeOffset ChangedAt);
