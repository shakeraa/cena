// =============================================================================
// Cena Platform — Classroom-assigned ExamTarget command + service (PRR-236)
//
// Teachers assign an ExamTarget to a whole classroom roster. The command is
// fanned out per enrolled student, each producing an ExamTargetAdded_V1
// event with Source=Classroom + AssignedById=teacherUserId +
// EnrollmentId=classroom-<classroomId> so ADR-0001 tenant-scoped audit
// reads can reconstruct "which teacher, which classroom" without joining
// back to the ClassroomDocument.
//
// Design notes (PRR-236 scope, locked to MVP):
//   - One teacher action → N student writes. Failures are collected
//     per-student; partial success is a valid outcome (roster churn,
//     optimistic-concurrency loss, etc).
//   - Idempotent on the (classroomId, examCode, track, sitting) tuple:
//     re-assigning to the same class produces a zero-new-writes result
//     with an "already-assigned" counter — the student-side uniqueness
//     invariant (ADR-0050 §5) guarantees this at the per-target level,
//     and the service folds that into a roster-level signal.
//   - Empty roster → warning only, no writes, no audit noise.
//   - Tenancy: this service does NOT verify the caller's cross-tenant
//     rights — that is the endpoint layer's job via TenantScope + claims.
//     The service trusts its inputs and just does the fan-out.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Teacher command: assign an ExamTarget to every student currently enrolled
/// in a given classroom. Produces one ExamTargetAdded_V1 per student
/// (Source=Classroom).
/// </summary>
/// <param name="InstituteId">ADR-0001 tenant — the institute the classroom
/// belongs to. Used to scope the enrollment event replay + audit.</param>
/// <param name="ClassroomId">Target classroom.</param>
/// <param name="TeacherUserId">Authenticated teacher id; lands as
/// <see cref="ExamTarget.AssignedById"/> on every fanned-out target.</param>
/// <param name="ExamCode">Catalog primary key.</param>
/// <param name="Track">Track within the exam (null when exam has no
/// track concept).</param>
/// <param name="Sitting">Primary sitting tuple.</param>
/// <param name="WeeklyHoursDefault">Per-student weekly hours to seed on
/// the new target. Students can later adjust via their own update path.</param>
/// <param name="QuestionPaperCodes">Ministry שאלון codes (PRR-243). Null
/// or empty is treated as empty. Bagrut family ⇒ must be non-empty.</param>
public sealed record AssignClassroomTargetCommand(
    string InstituteId,
    string ClassroomId,
    UserId TeacherUserId,
    ExamCode ExamCode,
    TrackCode? Track,
    SittingCode Sitting,
    int WeeklyHoursDefault,
    IReadOnlyList<string>? QuestionPaperCodes = null);

/// <summary>
/// Summary of a classroom assignment fan-out.
/// </summary>
/// <param name="ClassroomId">Target classroom (echoed for log correlation).</param>
/// <param name="ExamCode">Target exam code (echoed for log correlation).</param>
/// <param name="RosterSize">Total students found on the active roster.</param>
/// <param name="StudentsAssigned">Students newly assigned this run.</param>
/// <param name="StudentsAlreadyAssigned">Students who already carried an
/// active target with the same (examCode, track, sitting) tuple — the
/// idempotency signal per PRR-236 DoD.</param>
/// <param name="StudentsFailed">Students where the aggregate rejected the
/// add (e.g. per-student 40h budget blown). Callers typically log these
/// for teacher review.</param>
/// <param name="PerStudentResults">Per-student outcome for audit trail.</param>
/// <param name="Warning">Set when the roster was empty (no writes).</param>
public sealed record AssignClassroomTargetResult(
    string ClassroomId,
    string ExamCode,
    int RosterSize,
    int StudentsAssigned,
    int StudentsAlreadyAssigned,
    int StudentsFailed,
    IReadOnlyList<ClassroomTargetStudentOutcome> PerStudentResults,
    string? Warning = null);

/// <summary>
/// Per-student record of what happened when we tried to fan out the
/// classroom assignment to them.
/// </summary>
public sealed record ClassroomTargetStudentOutcome(
    string StudentAnonId,
    ClassroomTargetOutcomeKind Kind,
    ExamTargetId? TargetId = null,
    CommandError? Error = null);

/// <summary>Enumerates the three outcomes for a single fanned-out student.</summary>
public enum ClassroomTargetOutcomeKind
{
    /// <summary>Fresh <see cref="ExamTargetAdded_V1"/> emitted.</summary>
    Assigned = 0,

    /// <summary>The student already had an active target on the same
    /// (examCode, track, sitting) tuple — idempotent no-op.</summary>
    AlreadyAssigned = 1,

    /// <summary>Aggregate rejected the add (cap, budget, shape).
    /// <see cref="ClassroomTargetStudentOutcome.Error"/> carries
    /// the specific reason for audit.</summary>
    Failed = 2,
}

/// <summary>
/// Classroom-assigned target service. Fans a teacher-initiated
/// assignment to every currently-enrolled student via the existing
/// <see cref="IStudentPlanCommandHandler"/>.
/// </summary>
public interface IClassroomTargetAssignmentService
{
    /// <summary>
    /// Fan out the assignment to every currently-enrolled student in
    /// <see cref="AssignClassroomTargetCommand.ClassroomId"/>.
    /// </summary>
    Task<AssignClassroomTargetResult> AssignAsync(
        AssignClassroomTargetCommand cmd,
        CancellationToken ct = default);
}

/// <summary>
/// Classroom roster lookup abstraction — returns the list of
/// studentAnonIds currently enrolled as Active in a given classroom,
/// scoped to an institute. The production implementation lives in
/// Cena.Admin.Api.RosterImport / Cena.Actors.* and replays enrollment
/// events; tests supply a stub.
/// </summary>
public interface IClassroomRosterLookup
{
    /// <summary>
    /// Returns the active roster — every student currently in
    /// <paramref name="classroomId"/> within <paramref name="instituteId"/>.
    /// An empty result is a valid answer (no-roster classroom).
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveRosterAsync(
        string instituteId,
        string classroomId,
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation of
/// <see cref="IClassroomTargetAssignmentService"/>. Delegates
/// per-student writes to <see cref="IStudentPlanCommandHandler"/> and
/// roster resolution to <see cref="IClassroomRosterLookup"/>. Pure
/// composition — no I/O of its own.
/// </summary>
public sealed class ClassroomTargetAssignmentService : IClassroomTargetAssignmentService
{
    private readonly IStudentPlanCommandHandler _commandHandler;
    private readonly IClassroomRosterLookup _rosterLookup;

    /// <summary>Conventional enrollment-id prefix for the classroom-level
    /// scope tag on <see cref="ExamTarget.EnrollmentId"/>. Kept here
    /// (not in the command) so the service is the single source of truth.</summary>
    public const string ClassroomEnrollmentIdPrefix = "classroom-";

    /// <summary>
    /// Ctor. Both dependencies are required.
    /// </summary>
    public ClassroomTargetAssignmentService(
        IStudentPlanCommandHandler commandHandler,
        IClassroomRosterLookup rosterLookup)
    {
        _commandHandler = commandHandler
            ?? throw new ArgumentNullException(nameof(commandHandler));
        _rosterLookup = rosterLookup
            ?? throw new ArgumentNullException(nameof(rosterLookup));
    }

    /// <inheritdoc />
    public async Task<AssignClassroomTargetResult> AssignAsync(
        AssignClassroomTargetCommand cmd,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.InstituteId))
            throw new ArgumentException("InstituteId required", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.ClassroomId))
            throw new ArgumentException("ClassroomId required", nameof(cmd));

        var roster = await _rosterLookup
            .GetActiveRosterAsync(cmd.InstituteId, cmd.ClassroomId, ct)
            .ConfigureAwait(false);

        if (roster.Count == 0)
        {
            return new AssignClassroomTargetResult(
                ClassroomId: cmd.ClassroomId,
                ExamCode: cmd.ExamCode.Value,
                RosterSize: 0,
                StudentsAssigned: 0,
                StudentsAlreadyAssigned: 0,
                StudentsFailed: 0,
                PerStudentResults: Array.Empty<ClassroomTargetStudentOutcome>(),
                Warning: "roster-empty");
        }

        var enrollmentId = new EnrollmentId(
            ClassroomEnrollmentIdPrefix + cmd.ClassroomId);

        var outcomes = new List<ClassroomTargetStudentOutcome>(roster.Count);
        var assigned = 0;
        var already = 0;
        var failed = 0;

        foreach (var studentAnonId in roster)
        {
            ct.ThrowIfCancellationRequested();

            var add = new AddExamTargetCommand(
                StudentAnonId: studentAnonId,
                Source: ExamTargetSource.Classroom,
                AssignedById: cmd.TeacherUserId,
                EnrollmentId: enrollmentId,
                ExamCode: cmd.ExamCode,
                Track: cmd.Track,
                Sitting: cmd.Sitting,
                WeeklyHours: cmd.WeeklyHoursDefault,
                ReasonTag: null,
                QuestionPaperCodes: cmd.QuestionPaperCodes,
                PerPaperSittingOverride: null,
                MigrationSourceId: null,
                StudentAgeBand: null);

            var result = await _commandHandler.HandleAsync(add, ct).ConfigureAwait(false);

            if (result.Success)
            {
                assigned++;
                outcomes.Add(new ClassroomTargetStudentOutcome(
                    StudentAnonId: studentAnonId,
                    Kind: ClassroomTargetOutcomeKind.Assigned,
                    TargetId: result.TargetId));
            }
            else if (result.Error == CommandError.DuplicateTarget)
            {
                // Idempotency: the student already carries an active
                // target on the same (examCode, track, sitting) tuple.
                // ADR-0050 §5 uniqueness invariant gives us this for free.
                already++;
                outcomes.Add(new ClassroomTargetStudentOutcome(
                    StudentAnonId: studentAnonId,
                    Kind: ClassroomTargetOutcomeKind.AlreadyAssigned,
                    Error: null));
            }
            else
            {
                failed++;
                outcomes.Add(new ClassroomTargetStudentOutcome(
                    StudentAnonId: studentAnonId,
                    Kind: ClassroomTargetOutcomeKind.Failed,
                    Error: result.Error));
            }
        }

        return new AssignClassroomTargetResult(
            ClassroomId: cmd.ClassroomId,
            ExamCode: cmd.ExamCode.Value,
            RosterSize: roster.Count,
            StudentsAssigned: assigned,
            StudentsAlreadyAssigned: already,
            StudentsFailed: failed,
            PerStudentResults: outcomes,
            Warning: null);
    }
}
