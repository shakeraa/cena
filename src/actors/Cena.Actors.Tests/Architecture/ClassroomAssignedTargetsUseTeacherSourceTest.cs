// =============================================================================
// Cena Platform — Architecture test: PRR-236 classroom assignments emit
// Source=Classroom events only
//
// Hard invariant (ADR-0050 §Q3 + PRR-236):
//
//   The IClassroomTargetAssignmentService fan-out MUST produce ExamTarget
//   events whose Source=ExamTargetSource.Classroom. Any accidental regression
//   to Source=Student or Source=Tenant would break (a) the student-facing
//   "who assigned this" Source badge on settings, (b) the lawful-basis
//   transparency path (ADR-0050 §Q3), and (c) the 24-month retention
//   distinction between teacher-declared and student-declared plan data.
//
// This test runs the real in-memory pipeline: a fabricated roster, a real
// StudentPlanCommandHandler, and a real ClassroomTargetAssignmentService,
// and asserts the emitted events carry Source=Classroom.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class ClassroomAssignedTargetsUseTeacherSourceTest
{
    private sealed class FixedRosterLookup : IClassroomRosterLookup
    {
        private readonly IReadOnlyList<string> _roster;
        public FixedRosterLookup(params string[] roster) { _roster = roster; }
        public Task<IReadOnlyList<string>> GetActiveRosterAsync(
            string instituteId, string classroomId, CancellationToken ct = default)
            => Task.FromResult(_roster);
    }

    [Fact]
    public async Task ClassroomAssignment_produces_events_with_Source_Classroom()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(
            store,
            clock: () => DateTimeOffset.Parse("2026-04-21T10:00:00Z"));
        var roster = new FixedRosterLookup("stu-a", "stu-b", "stu-c");
        var svc = new ClassroomTargetAssignmentService(handler, roster);

        var cmd = new AssignClassroomTargetCommand(
            InstituteId: "inst-001",
            ClassroomId: "class-01",
            TeacherUserId: new UserId("teacher-42"),
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: new SittingCode("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHoursDefault: 4,
            QuestionPaperCodes: new[] { "035581" });

        var result = await svc.AssignAsync(cmd);

        Assert.Equal(3, result.StudentsAssigned);

        // Gather every ExamTargetAdded_V1 event across all student streams.
        var addedEvents = new[] { "stu-a", "stu-b", "stu-c" }
            .SelectMany(s => store.GetRawEvents(s).OfType<ExamTargetAdded_V1>())
            .ToList();

        Assert.Equal(3, addedEvents.Count);

        // Hard invariant: every event produced by the classroom fan-out
        // must carry Source=Classroom. Any deviation is a ship-blocker
        // per ADR-0050 §Q3.
        Assert.All(addedEvents, e
            => Assert.Equal(ExamTargetSource.Classroom, e.Target.Source));

        // The teacher id must flow through AssignedById on every event.
        Assert.All(addedEvents, e
            => Assert.Equal("teacher-42", e.Target.AssignedById.Value));

        // EnrollmentId must be non-null (ADR-0001 tenancy) and use the
        // canonical classroom prefix so audit consumers can map back.
        Assert.All(addedEvents, e =>
        {
            Assert.NotNull(e.Target.EnrollmentId);
            Assert.StartsWith(
                ClassroomTargetAssignmentService.ClassroomEnrollmentIdPrefix,
                e.Target.EnrollmentId!.Value.Value,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Service_ctor_rejects_null_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ClassroomTargetAssignmentService(null!, new FixedRosterLookup()));
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store);
        Assert.Throws<ArgumentNullException>(() =>
            new ClassroomTargetAssignmentService(handler, null!));
    }
}
