// =============================================================================
// Cena Platform — StudentPlan command DTOs + interface (prr-218, prr-243)
//
// Pure-data command records + the IStudentPlanCommandHandler interface.
// Split from the handler implementation so the core + QuestionPapers
// partials stay comfortably under the 500-LOC cap.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Command: add a new exam target to a student's plan.
/// </summary>
/// <param name="StudentAnonId">Plan owner.</param>
/// <param name="Source">Who is adding (Student | Classroom | Tenant |
/// Migration).</param>
/// <param name="AssignedById">Student / teacher / admin / system id.</param>
/// <param name="EnrollmentId">ADR-0001 scoping — required when
/// Source=Classroom or Tenant, must be null when Source=Student.</param>
/// <param name="ExamCode">Catalog primary key.</param>
/// <param name="Track">Track within the exam (null when exam has no
/// track concept).</param>
/// <param name="Sitting">Primary sitting tuple.</param>
/// <param name="WeeklyHours">Hours/week for this target (1..40).</param>
/// <param name="ReasonTag">Why (optional).</param>
/// <param name="QuestionPaperCodes">Ministry שאלון codes (PRR-243). Null
/// or empty is treated as empty list. Bagrut family ⇒ must be non-empty;
/// Standardized family ⇒ must be empty. Duplicates are rejected.</param>
/// <param name="PerPaperSittingOverride">Optional per-שאלון sitting
/// override map (PRR-243). Keys must be a subset of
/// <paramref name="QuestionPaperCodes"/>; values must differ from
/// <paramref name="Sitting"/>.</param>
/// <param name="MigrationSourceId">prr-219 idempotency key. Null for
/// non-migration commands.</param>
public sealed record AddExamTargetCommand(
    string StudentAnonId,
    ExamTargetSource Source,
    UserId AssignedById,
    EnrollmentId? EnrollmentId,
    ExamCode ExamCode,
    TrackCode? Track,
    SittingCode Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag,
    IReadOnlyList<string>? QuestionPaperCodes = null,
    IReadOnlyDictionary<string, SittingCode>? PerPaperSittingOverride = null,
    string? MigrationSourceId = null);

/// <summary>
/// Command: append a Ministry שאלון code to an existing active Bagrut
/// target (PRR-243).
/// </summary>
public sealed record AddQuestionPaperCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode,
    SittingCode? SittingOverride = null);

/// <summary>
/// Command: remove a Ministry שאלון code from an existing active
/// Bagrut target (PRR-243). Rejects if the removal would leave a
/// Bagrut target with zero papers.
/// </summary>
public sealed record RemoveQuestionPaperCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode);

/// <summary>
/// Command: set (create-or-replace) a per-שאלון sitting override on an
/// existing active target (PRR-243).
/// </summary>
public sealed record SetPerPaperSittingOverrideCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode,
    SittingCode Sitting);

/// <summary>
/// Command: clear a per-שאלון sitting override (PRR-243). Idempotent —
/// clearing a non-existent override succeeds with no event.
/// </summary>
public sealed record ClearPerPaperSittingOverrideCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    string PaperCode);

/// <summary>
/// Command: update an existing active target's mutable fields.
/// </summary>
public sealed record UpdateExamTargetCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    TrackCode? Track,
    SittingCode Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag);

/// <summary>
/// Command: archive an active target (terminal).
/// </summary>
public sealed record ArchiveExamTargetCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    ArchiveReason Reason);

/// <summary>
/// Command: mark an active target completed (terminal, specialised
/// archive reason).
/// </summary>
public sealed record CompleteExamTargetCommand(
    string StudentAnonId,
    ExamTargetId TargetId);

/// <summary>
/// Command: telemetry event — the scheduler ran a session against a
/// specific target. No state mutation; no invariants checked beyond
/// target existence.
/// </summary>
public sealed record ApplyExamTargetOverrideCommand(
    string StudentAnonId,
    ExamTargetId TargetId,
    string SessionId);

/// <summary>
/// Command handler + invariants. Stateless; delegates persistence to
/// <see cref="IStudentPlanAggregateStore"/>.
/// </summary>
public interface IStudentPlanCommandHandler
{
    /// <summary>Add a new exam target.</summary>
    Task<CommandResult> HandleAsync(AddExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Update an existing active target.</summary>
    Task<CommandResult> HandleAsync(UpdateExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Archive an active target.</summary>
    Task<CommandResult> HandleAsync(ArchiveExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Complete an active target.</summary>
    Task<CommandResult> HandleAsync(CompleteExamTargetCommand cmd, CancellationToken ct = default);

    /// <summary>Record a scheduler-override telemetry event.</summary>
    Task<CommandResult> HandleAsync(ApplyExamTargetOverrideCommand cmd, CancellationToken ct = default);

    /// <summary>Append a שאלון to a Bagrut target (PRR-243).</summary>
    Task<CommandResult> HandleAsync(AddQuestionPaperCommand cmd, CancellationToken ct = default);

    /// <summary>Remove a שאלון from a Bagrut target (PRR-243).</summary>
    Task<CommandResult> HandleAsync(RemoveQuestionPaperCommand cmd, CancellationToken ct = default);

    /// <summary>Set a per-שאלון sitting override (PRR-243).</summary>
    Task<CommandResult> HandleAsync(SetPerPaperSittingOverrideCommand cmd, CancellationToken ct = default);

    /// <summary>Clear a per-שאלון sitting override (PRR-243).</summary>
    Task<CommandResult> HandleAsync(ClearPerPaperSittingOverrideCommand cmd, CancellationToken ct = default);
}
