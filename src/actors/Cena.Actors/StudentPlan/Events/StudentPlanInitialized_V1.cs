// =============================================================================
// Cena Platform — StudentPlanInitialized_V1 (prr-218)
//
// Emitted exactly once per student, when the first write occurs against
// their StudentPlan stream. Lightweight marker: the stream's existence
// already implies "this student has a plan aggregate"; this event exists
// so projections + audit queries have a canonical "when did the student
// first opt into the multi-target world" timestamp without scanning the
// whole event log.
//
// The stream key format is `studentplan-{studentAnonId}` (unchanged from
// the legacy prr-148 aggregate — one stream per student).
//
// ADR-0050 coupling:
//   - §1 — normative shape stored here is the envelope only (student id
//     + initial timestamp). ExamTarget records arrive via
//     ExamTargetAdded_V1.
//   - §6 — retention is 24 months post-archive for any individual target;
//     the initialization event itself is retained for the life of the
//     student's plan (cleared on account deletion per ADR-0038 RTBF).
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// First-write marker on a StudentPlan stream. Emitted once per student.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id (already derived
/// at the command boundary from the JWT `student_id` claim).</param>
/// <param name="InitializedAt">Wall-clock of the initialization.</param>
public sealed record StudentPlanInitialized_V1(
    string StudentAnonId,
    DateTimeOffset InitializedAt);
