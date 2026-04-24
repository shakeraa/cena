// =============================================================================
// Cena Platform — ExamTarget* events (prr-222/223/229, ADR-0050)
//
// Minimal event set required for the mastery + RTBF + retention work in
// wave1c. The full event set (add/update/override) lands with prr-218;
// this file ships ONLY the events that the retention worker
// (ExamTargetRetentionWorker) and the erasure cascade
// (ExamTargetErasureCascade) need to reason about:
//
//   - ExamTargetAdded_V1   — birth of a target, gives the policy something
//                            to key the 24-month clock off when the target
//                            is later archived.
//   - ExamTargetArchived_V1 — terminal state for a target; ArchivedAtUtc
//                             is the anchor for the ADR-0050 §6 24-month
//                             post-archive retention clock enforced in
//                             prr-229.
//
// Both events are append-only per ADR-0038 (event-sourced RTBF); erasure
// is via subject-key tombstone crypto-shred, not row deletion.
// =============================================================================

using Cena.Actors.ExamTargets;

namespace Cena.Actors.ExamTargets.Events;

/// <summary>
/// A student (or their teacher on their behalf — ADR-0050 §9) added an
/// exam target to the student's plan. The 24-month retention policy
/// (ADR-0050 §6, prr-229) begins counting from <c>ArchivedAtUtc</c> on the
/// companion Archived event, NOT from this event; a never-archived target
/// has no retention deadline.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id (ADR-0038).</param>
/// <param name="ExamTargetCode">Catalog code (e.g. "bagrut-math-5yu").</param>
/// <param name="AddedAt">Wall-clock of the student's action.</param>
public sealed record ExamTargetAdded_V1(
    string StudentAnonId,
    ExamTargetCode ExamTargetCode,
    DateTimeOffset AddedAt);

/// <summary>
/// A student archived an exam target. This is the terminal event for the
/// target: no further Updated/Archived/OverrideApplied events may be
/// appended per ADR-0050 §5. The <see cref="ArchivedAtUtc"/> timestamp is
/// the anchor for the prr-229 24-month retention clock.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="ExamTargetCode">Catalog code of the target being archived.</param>
/// <param name="ArchivedAtUtc">Wall-clock UTC of archival (retention anchor).</param>
/// <param name="Reason">
/// Free-form but bounded by the UI's reason picker. Used only for audit;
/// not fed to any model. Never PII.
/// </param>
public sealed record ExamTargetArchived_V1(
    string StudentAnonId,
    ExamTargetCode ExamTargetCode,
    DateTimeOffset ArchivedAtUtc,
    string Reason);
