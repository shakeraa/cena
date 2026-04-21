// =============================================================================
// Cena Platform — ClassroomTargetAssignmentService tests (PRR-236)
//
// Unit tests for the teacher fan-out service. Covers:
//   1. Teacher assign → every enrolled student gets an ExamTargetAdded_V1
//      with Source=Classroom + AssignedById=teacher + EnrollmentId=classroom-<id>.
//   2. Idempotency: re-assigning the same (examCode, track, sitting) tuple
//      to the same classroom produces an "already assigned" summary with
//      zero new writes.
//   3. Empty roster → warning + no writes.
//   4. Student-side archive path works for a classroom-sourced target
//      (Source=Classroom does NOT lock the student out of archiving).
//
// Cross-tenant rejection (403) is exercised at the endpoint layer — see
// ClassroomTargetEndpointAuthzTests.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class ClassroomTargetAssignmentServiceTests
{
    private const string InstituteId = "inst-001";
    private const string ClassroomId = "class-math-5u";
    private const string TeacherId = "teacher-42";

    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    private static readonly SittingCode Bagrut2026Summer =
        new("תשפ״ו", SittingSeason.Summer, SittingMoed.A);

    private sealed class StubRosterLookup : IClassroomRosterLookup
    {
        public List<string> Students { get; } = new();
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> GetActiveRosterAsync(
            string instituteId, string classroomId, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<string>>(Students.ToArray());
        }
    }

    private static (ClassroomTargetAssignmentService svc,
                    InMemoryStudentPlanAggregateStore store,
                    StudentPlanCommandHandler handler,
                    StubRosterLookup roster)
        Build(params string[] roster)
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store, () => FixedNow);
        var lookup = new StubRosterLookup();
        lookup.Students.AddRange(roster);
        var svc = new ClassroomTargetAssignmentService(handler, lookup);
        return (svc, store, handler, lookup);
    }

    private static AssignClassroomTargetCommand SampleCmd(
        string examCode = "BAGRUT_MATH_5U",
        string? track = "5U",
        IReadOnlyList<string>? papers = null,
        int weeklyHours = 4)
        => new(
            InstituteId: InstituteId,
            ClassroomId: ClassroomId,
            TeacherUserId: new UserId(TeacherId),
            ExamCode: new ExamCode(examCode),
            Track: track is null ? null : new TrackCode(track),
            Sitting: Bagrut2026Summer,
            WeeklyHoursDefault: weeklyHours,
            QuestionPaperCodes: papers ?? new[] { "035581" });

    // ── DoD (a): teacher assigns → all enrolled students get target ──────

    [Fact]
    public async Task Assign_fans_out_classroom_target_to_every_enrolled_student()
    {
        var (svc, store, _, _) = Build("stu-a", "stu-b", "stu-c");

        var result = await svc.AssignAsync(SampleCmd());

        Assert.Null(result.Warning);
        Assert.Equal(3, result.RosterSize);
        Assert.Equal(3, result.StudentsAssigned);
        Assert.Equal(0, result.StudentsAlreadyAssigned);
        Assert.Equal(0, result.StudentsFailed);

        foreach (var studentId in new[] { "stu-a", "stu-b", "stu-c" })
        {
            var events = store.GetRawEvents(studentId);
            var added = Assert.Single(events.OfType<ExamTargetAdded_V1>());
            Assert.Equal(ExamTargetSource.Classroom, added.Target.Source);
            Assert.Equal(TeacherId, added.Target.AssignedById.Value);
            Assert.NotNull(added.Target.EnrollmentId);
            Assert.Equal("classroom-" + ClassroomId, added.Target.EnrollmentId!.Value.Value);
        }
    }

    // ── DoD (b): Idempotent on re-assign ─────────────────────────────────

    [Fact]
    public async Task Assign_twice_is_idempotent_per_tuple()
    {
        var (svc, store, _, _) = Build("stu-a", "stu-b");

        var first = await svc.AssignAsync(SampleCmd());
        Assert.Equal(2, first.StudentsAssigned);

        var second = await svc.AssignAsync(SampleCmd());

        Assert.Equal(0, second.StudentsAssigned);
        Assert.Equal(2, second.StudentsAlreadyAssigned);
        Assert.Equal(0, second.StudentsFailed);

        // No duplicate target events on any stream.
        foreach (var studentId in new[] { "stu-a", "stu-b" })
        {
            var added = store.GetRawEvents(studentId).OfType<ExamTargetAdded_V1>();
            Assert.Single(added);
        }
    }

    [Fact]
    public async Task Assign_different_tuple_after_first_creates_second_target()
    {
        var (svc, store, _, _) = Build("stu-a");

        await svc.AssignAsync(SampleCmd(examCode: "BAGRUT_MATH_5U"));
        var second = await svc.AssignAsync(SampleCmd(
            examCode: "BAGRUT_ENGLISH",
            track: null,
            papers: new[] { "016581" },
            weeklyHours: 3));

        Assert.Equal(1, second.StudentsAssigned);
        Assert.Equal(0, second.StudentsAlreadyAssigned);

        var added = store.GetRawEvents("stu-a").OfType<ExamTargetAdded_V1>().ToList();
        Assert.Equal(2, added.Count);
    }

    // ── Empty roster → warning, no writes ───────────────────────────────

    [Fact]
    public async Task Assign_empty_roster_returns_warning_and_writes_nothing()
    {
        var (svc, store, _, lookup) = Build();

        var result = await svc.AssignAsync(SampleCmd());

        Assert.Equal("roster-empty", result.Warning);
        Assert.Equal(0, result.RosterSize);
        Assert.Equal(0, result.StudentsAssigned);
        Assert.Empty(result.PerStudentResults);
        Assert.Equal(1, lookup.CallCount);

        // No writes for any student.
        Assert.Empty(store.GetRawEvents("anyone-at-all"));
    }

    // ── DoD (d): student can archive a classroom-sourced target ─────────

    [Fact]
    public async Task Student_can_archive_classroom_sourced_target()
    {
        // Teacher assigns.
        var (svc, _, handler, _) = Build("stu-a");
        var assign = await svc.AssignAsync(SampleCmd());
        var outcome = Assert.Single(assign.PerStudentResults);
        Assert.Equal(ClassroomTargetOutcomeKind.Assigned, outcome.Kind);
        var targetId = outcome.TargetId;
        Assert.NotNull(targetId);

        // Student archives via the regular archive command (ArchiveReason.StudentDeclined).
        var archive = await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentAnonId: "stu-a",
            TargetId: targetId!.Value,
            Reason: ArchiveReason.StudentDeclined));

        Assert.True(archive.Success);
    }

    // ── Shape contracts ─────────────────────────────────────────────────

    [Fact]
    public async Task Service_rejects_missing_institute_id()
    {
        var (svc, _, _, _) = Build("stu-a");
        await Assert.ThrowsAsync<ArgumentException>(async () => await svc.AssignAsync(new AssignClassroomTargetCommand(
            InstituteId: "",
            ClassroomId: ClassroomId,
            TeacherUserId: new UserId(TeacherId),
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: Bagrut2026Summer,
            WeeklyHoursDefault: 4,
            QuestionPaperCodes: new[] { "035581" })));
    }

    [Fact]
    public async Task Enrollment_id_uses_classroom_prefix()
    {
        var (svc, store, _, _) = Build("stu-x");
        await svc.AssignAsync(SampleCmd());

        var added = Assert.Single(store.GetRawEvents("stu-x").OfType<ExamTargetAdded_V1>());
        Assert.Equal(
            ClassroomTargetAssignmentService.ClassroomEnrollmentIdPrefix + ClassroomId,
            added.Target.EnrollmentId!.Value.Value);
    }

    // ── Per-student failure passes through ──────────────────────────────

    [Fact]
    public async Task Assign_records_per_student_failure_when_aggregate_rejects()
    {
        var (svc, _, handler, _) = Build("stu-a");

        // Seed stu-a with an existing 38-hour active target so the 4-hour
        // default would push them over the 40h budget cap (ADR-0050 §5).
        var preExisting = await handler.HandleAsync(new AddExamTargetCommand(
            StudentAnonId: "stu-a",
            Source: ExamTargetSource.Student,
            AssignedById: new UserId("stu-a"),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_ENGLISH"),
            Track: null,
            Sitting: Bagrut2026Summer,
            WeeklyHours: 38,
            ReasonTag: null,
            QuestionPaperCodes: new[] { "016581" }));
        Assert.True(preExisting.Success);

        var result = await svc.AssignAsync(SampleCmd(weeklyHours: 4));

        Assert.Equal(1, result.RosterSize);
        Assert.Equal(0, result.StudentsAssigned);
        Assert.Equal(1, result.StudentsFailed);
        var outcome = Assert.Single(result.PerStudentResults);
        Assert.Equal(ClassroomTargetOutcomeKind.Failed, outcome.Kind);
        Assert.Equal(CommandError.WeeklyBudgetExceeded, outcome.Error);
    }
}
